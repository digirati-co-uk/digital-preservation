// logical-structmap.js
// Manages client-side state and rendering for logical structMap editing.

const logicalStructMapState = {};  // { [structmapId]: LogicalRange (deep clone) }

let logicalStructMapDirty = false;

let addFilesTargetStructmapId = null;
let addFilesTargetRangeId = null;

let editRangeCallback = null;
let editRangeModalShown = false;

// Used when modsModal is opened for a logical range
let modsTargetStructmapId = null;
let modsTargetRangeId = null;

let dragState = null; // { structmapId, rangeId, localPath }

let editFilePointerCallback = null;

document.addEventListener('DOMContentLoaded', () => {
    if (typeof logicalStructMapsData === 'undefined' || !logicalStructMapsData.length) return;

    for (const lsm of logicalStructMapsData) {
        logicalStructMapState[lsm.id] = structuredClone(lsm);
        renderLogicalStructMap(lsm.id);
    }

    createFilePickerModal();
    createEditRangeModal();
    createEditFilePointerModal();
    wireModsModalIntercept();
    injectDragDropStyles();

    document.addEventListener('dragend', () => { clearDragVisuals(); dragState = null; });

    // Warn on navigation/refresh when there are unsaved changes
    window.addEventListener('beforeunload', e => {
        if (logicalStructMapDirty) {
            e.preventDefault();
        }
    });

    // Wire save forms: populate hidden JSON input before submit, clear dirty flag and indicator
    document.querySelectorAll('[id^="logicalStructMapJson_"]').forEach(input => {
        const structmapId = input.id.replace('logicalStructMapJson_', '');
        const form = input.closest('form');
        if (form) {
            form.addEventListener('submit', () => {
                input.value = JSON.stringify(logicalStructMapState[structmapId]);
                logicalStructMapDirty = false;
                document.getElementById(`logicalTab_${structmapId}-tab`)
                    ?.querySelector('.dirty-indicator')?.remove();
                const saveBtn = form.querySelector('button[type="submit"]');
                saveBtn.disabled = true;
                saveBtn.classList.replace('btn-warning', 'btn-primary');
            });
        }
    });
});

// ----------------------------------------------------------------
// Rendering
// ----------------------------------------------------------------

function renderLogicalStructMap(structmapId) {
    const container = document.getElementById(`logicalEditor_${structmapId}`);
    if (!container) return;
    const lsm = logicalStructMapState[structmapId];
    container.innerHTML = '';
    if (!lsm) return;

    const wrapper = document.createElement('div');
    wrapper.classList.add('table-responsive', 'small');
    const table = document.createElement('table');
    table.classList.add('table', 'table-hover', 'table-striped', 'table-sm', 'deposit-table');
    const tbody = document.createElement('tbody');
    renderRangeRows(lsm, structmapId, tbody, 1, true);
    table.appendChild(tbody);
    wrapper.appendChild(table);
    container.appendChild(wrapper);
}

function formatTime(seconds) {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
    return `${m}:${String(s).padStart(2, '0')}`;
}

function fpAnnotation(fp) {
    const parts = [];
    if (fp.beginTime != null || fp.endTime != null) {
        const begin = fp.beginTime != null ? formatTime(fp.beginTime) : '?';
        const end   = fp.endTime   != null ? formatTime(fp.endTime)   : '?';
        parts.push({ prefix: '⏱', label: 'Time', text: `${begin} – ${end}` });
    }
    if (fp.region != null) {
        const r = fp.region;
        parts.push({ prefix: '▭', label: 'Region', text: `${r.x1},${r.y1} – ${r.x2},${r.y2}` });
    }
    if (!parts.length) return null;
    const small = document.createElement('small');
    small.className = 'text-muted ms-3';
    parts.forEach(({ prefix, label, text }, i) => {
        if (i > 0) small.appendChild(document.createTextNode('   '));
        const icon = document.createElement('span');
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = prefix;
        const srLabel = document.createElement('span');
        srLabel.className = 'visually-hidden';
        srLabel.textContent = `${label}: `;
        small.appendChild(icon);
        small.appendChild(srLabel);
        small.appendChild(document.createTextNode(text));
    });
    return small;
}

function renderRangeRows(range, structmapId, tbody, depth, isRoot) {
    // --- Range row ---
    const tr = document.createElement('tr');

    const actionsTd = document.createElement('td');
    actionsTd.classList.add('dep-actions');
    const actionsDiv = document.createElement('div');
    actionsDiv.classList.add('d-flex', 'gap-1', 'align-items-center');
    const rangeUpBtn = makeActionLink('arrow-up', 'Move up',
        () => moveRange(structmapId, range.id, -1));
    const rangeDownBtn = makeActionLink('arrow-down', 'Move down',
        () => moveRange(structmapId, range.id, 1));
    if (isRoot) {
        rangeUpBtn.style.visibility = 'hidden';
        rangeUpBtn.disabled = true;
        rangeDownBtn.style.visibility = 'hidden';
        rangeDownBtn.disabled = true;
    }
    actionsDiv.appendChild(rangeUpBtn);
    actionsDiv.appendChild(rangeDownBtn);
    actionsDiv.appendChild(makeActionLink('folder-plus', 'Add child range',
        () => addChildRange(structmapId, range.id)));
    actionsDiv.appendChild(makeActionLink('file-earmark-plus', 'Add files',
        () => openFilePickerModal(structmapId, range.id)));
    actionsDiv.appendChild(makeActionLink('info-square', 'Metadata',
        () => openModsModalForRange(structmapId, range.id)));
    actionsDiv.appendChild(makeActionLink('pencil', 'Edit name and type',
        () => openEditRangeModal(range.name ?? '', range.type ?? 'Item', ({ name, type }) => {
            range.name = name || null;
            range.type = type;
            markDirty(structmapId);
            renderLogicalStructMap(structmapId);
        })));
    actionsTd.appendChild(actionsDiv);
    tr.appendChild(actionsTd);

    const nameTd = document.createElement('td');
    nameTd.style.paddingLeft = `${1.3 * depth}rem`;
    nameTd.innerHTML = `<svg class="bi" aria-hidden="true"><use xlink:href="#folder"/></svg> `;
    const typeBadge = document.createElement('span');
    typeBadge.classList.add('badge', 'bg-secondary', 'me-1');
    typeBadge.textContent = range.type || '(no type)';
    nameTd.appendChild(typeBadge);
    const nameSpan = document.createElement('strong');
    nameSpan.textContent = range.name || range.id;
    nameTd.appendChild(nameSpan);
    const metaText = buildMetadataSummaryText(range);
    if (metaText) {
        const metaSpan = document.createElement('span');
        metaSpan.classList.add('text-body-secondary', 'ms-2');
        metaSpan.textContent = metaText;
        nameTd.appendChild(metaSpan);
    }
    tr.appendChild(nameTd);

    const deleteTd = document.createElement('td');
    deleteTd.style.width = '1%';
    deleteTd.style.textAlign = 'right';
    if (!isRoot) {
        deleteTd.appendChild(makeActionLink('trash', 'Delete range',
            () => removeRange(structmapId, range.id), 'link-danger'));
    }
    tr.appendChild(deleteTd);
    tr.dataset.rowType = 'range';
    tr.dataset.rangeId = range.id;
    tr.addEventListener('dragover', e => {
        if (!dragState) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
        clearDragVisuals();
        tr.classList.add('drag-target-range');
    });
    tr.addEventListener('dragleave', e => {
        if (!tr.contains(e.relatedTarget)) tr.classList.remove('drag-target-range');
    });
    tr.addEventListener('drop', e => onRangeDrop(e, structmapId, range.id));
    tbody.appendChild(tr);

    // --- File pointer rows ---
    for (const fp of range.files) {
        const fileTr = document.createElement('tr');
        fileTr.draggable = true;
        fileTr.dataset.rowType = 'file';
        fileTr.dataset.rangeId = range.id;
        fileTr.dataset.filePath = fp.localPath;
        fileTr.addEventListener('dragstart', e => {
            dragState = { structmapId, rangeId: range.id, localPath: fp.localPath };
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', fp.localPath);
            fileTr.classList.add('file-dragging');
        });
        fileTr.addEventListener('dragover', e => {
            if (!dragState) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            clearDragVisuals();
            const mid = fileTr.getBoundingClientRect().top + fileTr.getBoundingClientRect().height / 2;
            fileTr.classList.add(e.clientY < mid ? 'drop-above' : 'drop-below');
        });
        fileTr.addEventListener('dragleave', e => {
            if (!fileTr.contains(e.relatedTarget)) fileTr.classList.remove('drop-above', 'drop-below');
        });
        fileTr.addEventListener('drop', e => onFileDrop(e, structmapId, range.id, fp.localPath, fileTr));

        const fileActionsTd = document.createElement('td');
        fileActionsTd.classList.add('dep-actions');
        const fileActionsDiv = document.createElement('div');
        fileActionsDiv.classList.add('d-flex', 'gap-1', 'align-items-center');
        const fileUpBtn = makeActionLink('arrow-up', 'Move up',
            () => moveFileInRange(structmapId, range.id, fp, -1));
        const fileDownBtn = makeActionLink('arrow-down', 'Move down',
            () => moveFileInRange(structmapId, range.id, fp, 1));
        if (range.files.length <= 1) {
            fileUpBtn.style.visibility = 'hidden';
            fileDownBtn.style.visibility = 'hidden';
        }
        fileActionsDiv.appendChild(fileUpBtn);
        fileActionsDiv.appendChild(fileDownBtn);
        fileActionsDiv.appendChild(makeActionLink('pencil', 'Edit extents',
            () => openEditFilePointerModal(fp, result => {
                fp.beginTime = result.beginTime;
                fp.endTime = result.endTime;
                fp.region = result.region;
                if (result.beginTime != null || result.endTime != null || result.region != null) {
                    fp.extraAreaAttributes = null;
                }
                markDirty(structmapId);
                renderLogicalStructMap(structmapId);
            })));
        fileActionsTd.appendChild(fileActionsDiv);
        fileTr.appendChild(fileActionsTd);

        const fileNameTd = document.createElement('td');
        fileNameTd.style.paddingLeft = `${1.3 * (depth + 1)}rem`;
        fileNameTd.innerHTML = `<svg class="bi" aria-hidden="true"><use xlink:href="#file-earmark"/></svg> `;
        fileNameTd.appendChild(document.createTextNode(fp.localPath));
        const annotation = fpAnnotation(fp);
        if (annotation) fileNameTd.appendChild(annotation);
        fileTr.appendChild(fileNameTd);

        const fileDeleteTd = document.createElement('td');
        fileDeleteTd.style.width = '1%';
        fileDeleteTd.style.textAlign = 'right';
        fileDeleteTd.appendChild(makeActionLink('trash', 'Remove from range',
            () => removeFileFromRange(structmapId, range.id, fp), 'link-danger'));
        fileTr.appendChild(fileDeleteTd);

        tbody.appendChild(fileTr);
    }

    // --- Child ranges (recursive) ---
    for (const child of range.ranges) {
        renderRangeRows(child, structmapId, tbody, depth + 1, false);
    }
}

function makeActionLink(icon, title, onClick, linkClass = 'link-primary') {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = `btn btn-sm btn-link p-0 ${linkClass}`;
    btn.setAttribute('aria-label', title);
    btn.innerHTML = `<svg class="bi" aria-hidden="true"><use xlink:href="#${icon}"/></svg>`;
    btn.addEventListener('click', onClick);
    return btn;
}

function buildMetadataSummaryText(range) {
    const parts = [];
    if (range.accessRestrictions?.length > 0)
        parts.push(range.accessRestrictions.join(', '));
    if (range.rightsStatement) {
        const label = getRightsShortLabel(range.rightsStatement);
        if (label) parts.push(label);
    }
    if (range.recordInfo?.recordIdentifiers?.length > 0) {
        const ri = range.recordInfo.recordIdentifiers;
        parts.push(ri.length === 1 ? `${ri[0].value} (${ri[0].source})` : `${ri.length} IDs`);
    }
    return parts.length ? parts.join(' · ') : null;
}

// ----------------------------------------------------------------
// Tree traversal helpers
// ----------------------------------------------------------------

function findRange(root, id) {
    if (root.id === id) return root;
    for (const child of root.ranges || []) {
        const found = findRange(child, id);
        if (found) return found;
    }
    return null;
}

function findParentAndIndex(root, id) {
    for (let i = 0; i < (root.ranges || []).length; i++) {
        if (root.ranges[i].id === id) return { parent: root, index: i };
        const found = findParentAndIndex(root.ranges[i], id);
        if (found) return found;
    }
    return null;
}

function findStructmapIdForRange(rangeId) {
    for (const [smId, root] of Object.entries(logicalStructMapState)) {
        if (findRange(root, rangeId)) return smId;
    }
    return null;
}

// ----------------------------------------------------------------
// Operations
// ----------------------------------------------------------------

function markDirty(structmapId) {
    logicalStructMapDirty = true;
    const tab = document.getElementById(`logicalTab_${structmapId}-tab`);
    if (tab && !tab.querySelector('.dirty-indicator')) {
        const dot = document.createElement('span');
        dot.className = 'dirty-indicator me-1 dirty-pulse';
        dot.title = 'Unsaved changes';
        dot.textContent = '●';
        dot.style.color = 'var(--bs-warning)';
        tab.prepend(dot);

        // Announce the transition to a dirty state once per change to unsaved (not on every
        // subsequent edit) so screen reader users are told the Save button is now enabled.
        const statusRegion = document.getElementById('logicalStructMapStatus');
        if (statusRegion) {
            const tabName = tab.querySelector('span')?.textContent?.trim() || 'logical structure';
            statusRegion.textContent = `Unsaved changes in ${tabName}. Save button enabled.`;
        }
    }
    const saveBtn = document.getElementById(`logicalStructMapJson_${structmapId}`)
        ?.closest('form')?.querySelector('button[type="submit"]');
    if (saveBtn) {
        saveBtn.disabled = false;
        saveBtn.classList.replace('btn-primary', 'btn-warning');
    }
}

function addChildRange(structmapId, parentId) {
    const root = logicalStructMapState[structmapId];
    const parent = findRange(root, parentId);
    if (!parent) return;
    openEditRangeModal('', 'Item', ({ name, type }) => {
        parent.ranges.push({
            id: 'LOG_' + Date.now(),
            type,
            name: name || null,
            ranges: [],
            files: [],
            effectiveAccessRestrictions: []
        });
        markDirty(structmapId);
        renderLogicalStructMap(structmapId);
    });
}

function removeRange(structmapId, id) {
    const root = logicalStructMapState[structmapId];
    const result = findParentAndIndex(root, id);
    if (!result) return;
    if (!confirm('Remove this range and all its children?')) return;
    result.parent.ranges.splice(result.index, 1);
    markDirty(structmapId);
    renderLogicalStructMap(structmapId);
}

function moveRange(structmapId, id, direction) {
    const root = logicalStructMapState[structmapId];
    const result = findParentAndIndex(root, id);
    if (!result) return;
    const { parent, index } = result;
    const newIndex = index + direction;
    if (newIndex < 0 || newIndex >= parent.ranges.length) return;
    const [item] = parent.ranges.splice(index, 1);
    parent.ranges.splice(newIndex, 0, item);
    markDirty(structmapId);
    renderLogicalStructMap(structmapId);
}

function moveFileInRange(structmapId, rangeId, fp, direction) {
    const root = logicalStructMapState[structmapId];
    const range = findRange(root, rangeId);
    if (!range) return;
    const index = range.files.indexOf(fp);
    if (index === -1) return;
    const newIndex = index + direction;
    if (newIndex < 0 || newIndex >= range.files.length) return;
    const [item] = range.files.splice(index, 1);
    range.files.splice(newIndex, 0, item);
    markDirty(structmapId);
    renderLogicalStructMap(structmapId);
}

function addFilesToRange(structmapId, rangeId, localPaths) {
    const root = logicalStructMapState[structmapId];
    const range = findRange(root, rangeId);
    if (!range) return;
    for (const lp of localPaths) {
        if (!range.files.some(f => f.localPath === lp)) {
            range.files.push({ localPath: lp });
        }
    }
    markDirty(structmapId);
    renderLogicalStructMap(structmapId);
}

function removeFileFromRange(structmapId, rangeId, fp) {
    const root = logicalStructMapState[structmapId];
    const range = findRange(root, rangeId);
    if (!range) return;
    range.files = range.files.filter(f => f !== fp);
    markDirty(structmapId);
    renderLogicalStructMap(structmapId);
}

function setRangeMetadata(structmapId, rangeId, accessRestrictions, rightsStatementUri, recordIdentifiers) {
    const root = logicalStructMapState[structmapId];
    const range = findRange(root, rangeId);
    if (!range) return;
    range.accessRestrictions = accessRestrictions.length > 0 ? accessRestrictions : null;
    range.rightsStatement = rightsStatementUri || null;
    range.recordInfo = recordIdentifiers.length > 0
        ? { recordIdentifiers }
        : null;
    markDirty(structmapId);
    renderLogicalStructMap(structmapId);
}

// ----------------------------------------------------------------
// Edit range modal (name + type)
// ----------------------------------------------------------------

function createEditRangeModal() {
    const modal = document.createElement('div');
    modal.classList.add('modal', 'fade');
    modal.id = 'editRangeModal';
    modal.setAttribute('tabindex', '-1');
    modal.setAttribute('aria-hidden', 'true');
    modal.setAttribute('aria-labelledby', 'editRangeModalTitle');
    modal.innerHTML = `
        <div class="modal-dialog">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title" id="editRangeModalTitle">Edit range</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <div class="mb-3">
                        <label for="editRangeName" class="form-label">Name</label>
                        <input type="text" class="form-control" id="editRangeName" placeholder="Range name">
                    </div>
                    <div class="mb-3">
                        <label for="editRangeType" class="form-label">Type</label>
                        <select class="form-select" id="editRangeType"></select>
                    </div>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
                    <button type="button" class="btn btn-primary" id="editRangeConfirmBtn">Save</button>
                </div>
            </div>
        </div>`;
    document.body.appendChild(modal);

    document.getElementById('editRangeConfirmBtn').addEventListener('click', () => {
        const name = document.getElementById('editRangeName').value.trim();
        const type = document.getElementById('editRangeType').value;
        const modalEl = document.getElementById('editRangeModal');
        // Use getOrCreateInstance rather than getInstance - if Bootstrap doesn't already have a
        // live Modal instance attached to this element, getInstance returns null and calling
        // .hide() on it throws, silently leaving the modal open with no visible error.
        const instance = bootstrap.Modal.getOrCreateInstance(modalEl);
        // Bootstrap's Modal.hide() is a documented no-op while the modal is still mid-way through
        // its own show transition (it checks an internal _isTransitioning flag and just returns).
        // The ~300ms fade-in from .show() can still be running when Save is clicked - a real user
        // clicking fast, or Playwright filling the form and clicking, can both beat it - so hide()
        // silently does nothing and the modal never closes. Deferring to shown.bs.modal guarantees
        // the transition has genuinely finished before we ask Bootstrap to hide it.
        if (editRangeModalShown) {
            instance.hide();
        } else {
            modalEl.addEventListener('shown.bs.modal', () => instance.hide(), { once: true });
        }
        if (editRangeCallback) editRangeCallback({ name, type });
        editRangeCallback = null;
    });

    document.getElementById('editRangeModal').addEventListener('shown.bs.modal', () => {
        editRangeModalShown = true;
    });

    // Clear callback if modal is dismissed without saving
    document.getElementById('editRangeModal').addEventListener('hidden.bs.modal', () => {
        editRangeCallback = null;
        editRangeModalShown = false;
    });
}

function openEditRangeModal(currentName, currentType, onConfirm) {
    editRangeCallback = onConfirm;
    document.getElementById('editRangeName').value = currentName;
    const typeSelect = document.getElementById('editRangeType');
    // Rebuild options, adding the existing type if it isn't in the configured list
    typeSelect.innerHTML = '';
    const typesToShow = rangeTypes.includes(currentType)
        ? rangeTypes
        : [...rangeTypes, currentType];
    for (const t of typesToShow) {
        const option = document.createElement('option');
        option.value = t;
        option.textContent = t;
        typeSelect.appendChild(option);
    }
    typeSelect.value = currentType || rangeTypes[0];

    bootstrap.Modal.getOrCreateInstance(document.getElementById('editRangeModal')).show();
}

// ----------------------------------------------------------------
// modsModal integration for logical ranges
// ----------------------------------------------------------------

function wireModsModalIntercept() {
    const modsModal = document.getElementById('modsModal');
    if (!modsModal) return;
    const modsForm = modsModal.querySelector('form');
    if (!modsForm) return;

    // Capture phase: fires before site.js single-submit listener
    modsForm.addEventListener('submit', function (e) {
        const ctx = document.getElementById('modsContext')?.value ?? '';
        if (!ctx.startsWith('LOG_')) return;  // physical file — let it submit normally

        e.preventDefault();
        e.stopImmediatePropagation();

        const smId = modsTargetStructmapId ?? findStructmapIdForRange(ctx);
        if (!smId) { bootstrap.Modal.getInstance(modsModal)?.hide(); return; }

        const accessRestrictions = readAccessRestrictionsFromForm();
        const rightsStatementUri = readRightsStatementUriFromForm();
        const recordIdentifiers = readRecordIdentifiersFromForm();

        setRangeMetadata(smId, ctx, accessRestrictions, rightsStatementUri, recordIdentifiers);
        bootstrap.Modal.getInstance(modsModal)?.hide();
    }, { capture: true });
}

function openModsModalForRange(structmapId, rangeId) {
    const modsContextEl = document.getElementById('modsContext');
    const modsContextIsFileEl = document.getElementById('modsContextIsFile');
    if (!modsContextEl) return;

    modsTargetStructmapId = structmapId;
    modsTargetRangeId = rangeId;

    modsContextEl.value = rangeId;
    if (modsContextIsFileEl) modsContextIsFileEl.value = 'false';

    // Build a fake launcher element with the range's metadata as data attributes
    const root = logicalStructMapState[structmapId];
    const range = root ? findRange(root, rangeId) : null;
    const launcher = document.createElement('span');
    launcher.dataset.access = (range?.accessRestrictions ?? []).join(',');
    launcher.dataset.rights = getRightsShortLabel(range?.rightsStatement) ?? '';
    launcher.dataset.recordinfo = toRecordInfoCompact(range?.recordInfo) ?? '';
    launcher.dataset.toolOutput = '';

    if (typeof populateModsModalFromAttributes === 'function') {
        populateModsModalFromAttributes(launcher);
    }

    bootstrap.Modal.getOrCreateInstance(document.getElementById('modsModal')).show();
}

// ----------------------------------------------------------------
// Reading form values
// ----------------------------------------------------------------

function readAccessRestrictionsFromForm() {
    const sel = document.getElementById('accessRestrictionsSelect');
    if (!sel) return [];
    return Array.from(sel.selectedOptions).map(o => o.value);
}

function readRightsStatementUriFromForm() {
    const sel = document.getElementById('rightsStatementSelect');
    return sel?.value || null;
}

function readRecordIdentifiersFromForm() {
    const result = [];
    document.querySelectorAll('.record-info-container').forEach(container => {
        const index = container.dataset.recordInfoIndex;
        const sourceEl = document.getElementById(`recordInfoSourceSelect_${index}`);
        const valueEl = document.getElementById(`recordInfoValue_${index}`);
        if (sourceEl && valueEl?.value.trim()) {
            result.push({ source: sourceEl.value, value: valueEl.value.trim() });
        }
    });
    return result;
}

// ----------------------------------------------------------------
// Compact format helpers
// ----------------------------------------------------------------

function getRightsShortLabel(uri) {
    if (!uri || typeof rightsStatements === 'undefined') return null;
    for (const [key, rs] of Object.entries(rightsStatements)) {
        if (rs.value === uri) return key;
    }
    return uri;
}

function toRecordInfoCompact(recordInfo) {
    if (!recordInfo?.recordIdentifiers?.length) return null;
    return recordInfo.recordIdentifiers
        .map(ri => `${ri.value}(${ri.source})`)
        .join('-|-');
}

// ----------------------------------------------------------------
// File picker modal
// ----------------------------------------------------------------

function createFilePickerModal() {
    const modal = document.createElement('div');
    modal.classList.add('modal', 'fade');
    modal.id = 'addFilesToRangeModal';
    modal.setAttribute('tabindex', '-1');
    modal.setAttribute('aria-hidden', 'true');
    modal.setAttribute('aria-labelledby', 'addFilesToRangeModalTitle');
    modal.innerHTML = `
        <div class="modal-dialog modal-lg">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title" id="addFilesToRangeModalTitle">
                        <svg class="bi" aria-hidden="true"><use xlink:href="#file-earmark-plus"/></svg>
                        <span class="ms-2">Add files to range</span>
                    </h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <input type="text" id="filePickerSearch" class="form-control mb-2" placeholder="Filter files...">
                    <div class="form-check border-bottom pb-2 mb-1">
                        <input type="checkbox" class="form-check-input" id="filePickerSelectAll">
                        <label class="form-check-label small fw-semibold" for="filePickerSelectAll">Select all filtered</label>
                    </div>
                    <div id="filePickerList" style="max-height:50vh;overflow-y:auto;"></div>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
                    <button type="button" class="btn btn-primary" id="addFilesConfirmBtn">Add selected</button>
                </div>
            </div>
        </div>`;
    document.body.appendChild(modal);

    document.getElementById('filePickerSearch').addEventListener('input', e => {
        filterFilePickerList(e.target.value);
        updateFilePickerSelectAll();
    });

    document.getElementById('filePickerSelectAll').addEventListener('change', e => {
        const checked = e.target.checked;
        document.querySelectorAll('#filePickerList .file-picker-item').forEach(item => {
            if (item.style.display === 'none') return;
            const cb = item.querySelector('input[type=checkbox]');
            if (cb) cb.checked = checked;
        });
    });

    document.getElementById('filePickerList').addEventListener('change', () => {
        updateFilePickerSelectAll();
    });

    document.getElementById('addFilesConfirmBtn').addEventListener('click', () => {
        const checked = document.querySelectorAll('#filePickerList input[type=checkbox]:checked');
        const paths = Array.from(checked).map(cb => cb.value);
        if (addFilesTargetStructmapId && addFilesTargetRangeId) {
            addFilesToRange(addFilesTargetStructmapId, addFilesTargetRangeId, paths);
        }
        bootstrap.Modal.getInstance(document.getElementById('addFilesToRangeModal')).hide();
    });
}

function filterFilePickerList(filter) {
    document.querySelectorAll('#filePickerList .file-picker-item').forEach(item => {
        const label = item.querySelector('label');
        const visible = !filter || label.textContent.toLowerCase().includes(filter.toLowerCase());
        item.style.display = visible ? '' : 'none';
    });
}

function updateFilePickerSelectAll() {
    const selectAll = document.getElementById('filePickerSelectAll');
    if (!selectAll) return;
    const visible = Array.from(document.querySelectorAll('#filePickerList .file-picker-item'))
        .filter(item => item.style.display !== 'none')
        .map(item => item.querySelector('input[type=checkbox]'))
        .filter(Boolean);
    if (visible.length === 0) {
        selectAll.checked = false;
        selectAll.indeterminate = false;
    } else {
        const checkedCount = visible.filter(cb => cb.checked).length;
        selectAll.indeterminate = checkedCount > 0 && checkedCount < visible.length;
        selectAll.checked = checkedCount === visible.length;
    }
}

function openFilePickerModal(structmapId, rangeId) {
    addFilesTargetStructmapId = structmapId;
    addFilesTargetRangeId = rangeId;

    const list = document.getElementById('filePickerList');
    list.innerHTML = '';
    document.getElementById('filePickerSearch').value = '';
    const selectAll = document.getElementById('filePickerSelectAll');
    if (selectAll) { selectAll.checked = false; selectAll.indeterminate = false; }

    const files = (typeof physicalFilePaths === 'undefined' ? [] : physicalFilePaths).filter(Boolean);
    const root = logicalStructMapState[structmapId];
    const range = root ? findRange(root, rangeId) : null;
    const alreadyAdded = new Set((range?.files || []).map(f => f.localPath));

    for (const path of files) {
        const item = document.createElement('div');
        item.classList.add('file-picker-item', 'form-check');
        const safeId = 'fp_' + path.replaceAll(/[^a-zA-Z0-9]/g, '_');
        const cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.classList.add('form-check-input');
        cb.value = path;
        cb.id = safeId;
        cb.checked = alreadyAdded.has(path);
        const label = document.createElement('label');
        label.classList.add('form-check-label', 'small');
        label.setAttribute('for', safeId);
        label.textContent = path;
        item.appendChild(cb);
        item.appendChild(label);
        list.appendChild(item);
    }

    bootstrap.Modal.getOrCreateInstance(document.getElementById('addFilesToRangeModal')).show();
}

// ----------------------------------------------------------------
// Edit file pointer modal (time segment + area extents)
// ----------------------------------------------------------------

function formatTimeForInput(seconds) {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

function parseTimeInput(str) {
    if (!str?.trim()) return null;
    const parts = str.trim().split(':');
    if (parts.length === 3) {
        const h = parseInt(parts[0], 10);
        const m = parseInt(parts[1], 10);
        const s = parseFloat(parts[2]);
        if (!isNaN(h) && !isNaN(m) && !isNaN(s) && h >= 0 && m >= 0 && s >= 0)
            return h * 3600 + m * 60 + s;
    } else if (parts.length === 2) {
        const m = parseInt(parts[0], 10);
        const s = parseFloat(parts[1]);
        if (!isNaN(m) && !isNaN(s) && m >= 0 && s >= 0)
            return m * 60 + s;
    }
    return null;
}

function createEditFilePointerModal() {
    const modal = document.createElement('div');
    modal.classList.add('modal', 'fade');
    modal.id = 'editFilePointerModal';
    modal.setAttribute('tabindex', '-1');
    modal.setAttribute('aria-hidden', 'true');
    modal.setAttribute('aria-labelledby', 'editFilePointerModalTitle');
    modal.innerHTML = `
        <div class="modal-dialog">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title" id="editFilePointerModalTitle">Edit file pointer extents</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <p class="small text-body-secondary mb-3" id="editFpPath"></p>
                    <div id="editFpExtraWarning" class="alert alert-warning small py-2 d-none">
                        This file pointer has area attributes that are not editable here and will be preserved unless you specify a time segment or area below.
                    </div>
                    <div class="mb-3">
                        <div class="form-check">
                            <input type="checkbox" class="form-check-input" id="editFpTimeEnabled">
                            <label class="form-check-label fw-semibold" for="editFpTimeEnabled">Time segment</label>
                        </div>
                        <div id="editFpTimeFields" class="mt-2 ms-4" style="display:none">
                            <div class="row g-2">
                                <div class="col">
                                    <label for="editFpTimeStart" class="form-label small">Start</label>
                                    <input type="text" class="form-control form-control-sm" id="editFpTimeStart" placeholder="HH:MM:SS">
                                </div>
                                <div class="col">
                                    <label for="editFpTimeEnd" class="form-label small">End</label>
                                    <input type="text" class="form-control form-control-sm" id="editFpTimeEnd" placeholder="HH:MM:SS">
                                </div>
                            </div>
                        </div>
                    </div>
                    <div class="mb-3">
                        <div class="form-check">
                            <input type="checkbox" class="form-check-input" id="editFpAreaEnabled">
                            <label class="form-check-label fw-semibold" for="editFpAreaEnabled">Area (rectangle)</label>
                        </div>
                        <div id="editFpAreaFields" class="mt-2 ms-4" style="display:none">
                            <div class="row g-2">
                                <div class="col"><label for="editFpX1" class="form-label small">X1</label><input type="number" class="form-control form-control-sm" id="editFpX1" min="0"></div>
                                <div class="col"><label for="editFpY1" class="form-label small">Y1</label><input type="number" class="form-control form-control-sm" id="editFpY1" min="0"></div>
                                <div class="col"><label for="editFpX2" class="form-label small">X2</label><input type="number" class="form-control form-control-sm" id="editFpX2" min="0"></div>
                                <div class="col"><label for="editFpY2" class="form-label small">Y2</label><input type="number" class="form-control form-control-sm" id="editFpY2" min="0"></div>
                            </div>
                        </div>
                    </div>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
                    <button type="button" class="btn btn-primary" id="editFpConfirmBtn">Save</button>
                </div>
            </div>
        </div>`;
    document.body.appendChild(modal);

    document.getElementById('editFpTimeEnabled').addEventListener('change', e => {
        document.getElementById('editFpTimeFields').style.display = e.target.checked ? '' : 'none';
    });
    document.getElementById('editFpAreaEnabled').addEventListener('change', e => {
        document.getElementById('editFpAreaFields').style.display = e.target.checked ? '' : 'none';
    });

    document.getElementById('editFpConfirmBtn').addEventListener('click', () => {
        const timeEnabled = document.getElementById('editFpTimeEnabled').checked;
        const areaEnabled = document.getElementById('editFpAreaEnabled').checked;
        const result = { beginTime: null, endTime: null, region: null };
        if (timeEnabled) {
            result.beginTime = parseTimeInput(document.getElementById('editFpTimeStart').value);
            result.endTime   = parseTimeInput(document.getElementById('editFpTimeEnd').value);
        }
        if (areaEnabled) {
            const x1 = parseInt(document.getElementById('editFpX1').value, 10);
            const y1 = parseInt(document.getElementById('editFpY1').value, 10);
            const x2 = parseInt(document.getElementById('editFpX2').value, 10);
            const y2 = parseInt(document.getElementById('editFpY2').value, 10);
            if (!isNaN(x1) && !isNaN(y1) && !isNaN(x2) && !isNaN(y2))
                result.region = { x1, y1, x2, y2 };
        }
        bootstrap.Modal.getInstance(document.getElementById('editFilePointerModal')).hide();
        if (editFilePointerCallback) editFilePointerCallback(result);
        editFilePointerCallback = null;
    });

    document.getElementById('editFilePointerModal').addEventListener('hidden.bs.modal', () => {
        editFilePointerCallback = null;
    });
}

function openEditFilePointerModal(fp, onConfirm) {
    editFilePointerCallback = onConfirm;

    document.getElementById('editFpPath').textContent = fp.localPath;

    const hasExtra = fp.extraAreaAttributes && Object.keys(fp.extraAreaAttributes).length > 0;
    document.getElementById('editFpExtraWarning').classList.toggle('d-none', !hasExtra);

    const hasTime = fp.beginTime != null || fp.endTime != null;
    document.getElementById('editFpTimeEnabled').checked = hasTime;
    document.getElementById('editFpTimeFields').style.display = hasTime ? '' : 'none';
    document.getElementById('editFpTimeStart').value = fp.beginTime != null ? formatTimeForInput(fp.beginTime) : '';
    document.getElementById('editFpTimeEnd').value   = fp.endTime   != null ? formatTimeForInput(fp.endTime)   : '';

    const hasArea = fp.region != null;
    document.getElementById('editFpAreaEnabled').checked = hasArea;
    document.getElementById('editFpAreaFields').style.display = hasArea ? '' : 'none';
    document.getElementById('editFpX1').value = fp.region?.x1 ?? '';
    document.getElementById('editFpY1').value = fp.region?.y1 ?? '';
    document.getElementById('editFpX2').value = fp.region?.x2 ?? '';
    document.getElementById('editFpY2').value = fp.region?.y2 ?? '';

    bootstrap.Modal.getOrCreateInstance(document.getElementById('editFilePointerModal')).show();
}

// ----------------------------------------------------------------
// Drag and drop
// ----------------------------------------------------------------

function injectDragDropStyles() {
    const style = document.createElement('style');
    style.textContent = `
        tr.file-dragging { opacity: 0.4; }
        tr.drop-above td  { box-shadow: inset 0  2px 0 var(--bs-primary); }
        tr.drop-below td  { box-shadow: inset 0 -2px 0 var(--bs-primary); }
        tr.drag-target-range td { background-color: var(--bs-primary-bg-subtle) !important; }
        tr[draggable="true"] { cursor: grab; }
    `;
    document.head.appendChild(style);
}

function clearDragVisuals() {
    document.querySelectorAll('.file-dragging, .drop-above, .drop-below, .drag-target-range')
        .forEach(el => el.classList.remove('file-dragging', 'drop-above', 'drop-below', 'drag-target-range'));
}

function onFileDrop(e, targetStructmapId, targetRangeId, targetLocalPath, targetTr) {
    e.preventDefault();
    if (!dragState) return;
    clearDragVisuals();

    const { structmapId: srcSmId, rangeId: srcRangeId, localPath: srcPath } = dragState;
    dragState = null;

    const root = logicalStructMapState[srcSmId];
    const srcRange = findRange(root, srcRangeId);
    const tgtRange = findRange(root, targetRangeId);
    if (!srcRange || !tgtRange) return;

    const mid = targetTr.getBoundingClientRect().top + targetTr.getBoundingClientRect().height / 2;
    const insertBefore = e.clientY < mid;

    // Remove from source, preserving the full FilePointer object
    const fpObj = srcRange.files.find(f => f.localPath === srcPath);
    srcRange.files = srcRange.files.filter(f => f.localPath !== srcPath);

    // Insert relative to target file
    const targetIdx = tgtRange.files.findIndex(f => f.localPath === targetLocalPath);
    const insertIdx = insertBefore ? targetIdx : targetIdx + 1;
    tgtRange.files.splice(insertIdx < 0 ? tgtRange.files.length : insertIdx, 0, fpObj);

    markDirty(srcSmId);
    renderLogicalStructMap(srcSmId);
}

function onRangeDrop(e, targetStructmapId, targetRangeId) {
    e.preventDefault();
    if (!dragState) return;
    clearDragVisuals();

    const { structmapId: srcSmId, rangeId: srcRangeId, localPath: srcPath } = dragState;
    dragState = null;

    const root = logicalStructMapState[srcSmId];
    const srcRange = findRange(root, srcRangeId);
    const tgtRange = findRange(root, targetRangeId);
    if (!srcRange || !tgtRange) return;

    const fpObj = srcRange.files.find(f => f.localPath === srcPath);
    srcRange.files = srcRange.files.filter(f => f.localPath !== srcPath);
    if (!tgtRange.files.some(f => f.localPath === srcPath)) {
        tgtRange.files.push(fpObj);
    }

    markDirty(srcSmId);
    renderLogicalStructMap(srcSmId);
}
