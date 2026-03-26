// logical-structmap.js
// Manages client-side state and rendering for logical structMap editing.

const logicalStructMapState = {};  // { [structmapId]: LogicalRange (deep clone) }

let addFilesTargetStructmapId = null;
let addFilesTargetRangeId = null;

let editRangeCallback = null;

// Used when modsModal is opened for a logical range
let modsTargetStructmapId = null;
let modsTargetRangeId = null;

document.addEventListener('DOMContentLoaded', () => {
    if (typeof logicalStructMapsData === 'undefined' || !logicalStructMapsData.length) return;

    for (const lsm of logicalStructMapsData) {
        logicalStructMapState[lsm.id] = JSON.parse(JSON.stringify(lsm));
        renderLogicalStructMap(lsm.id);
    }

    createFilePickerModal();
    createEditRangeModal();
    wireModsModalIntercept();

    // Wire save forms: populate hidden JSON input before submit
    document.querySelectorAll('[id^="logicalStructMapJson_"]').forEach(input => {
        const structmapId = input.id.replace('logicalStructMapJson_', '');
        const form = input.closest('form');
        if (form) {
            form.addEventListener('submit', () => {
                input.value = JSON.stringify(logicalStructMapState[structmapId]);
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
    if (lsm) container.appendChild(renderRange(lsm, structmapId, true));
}

function renderRange(range, structmapId, isRoot) {
    const div = document.createElement('div');
    div.classList.add('logical-range', 'border', 'rounded', 'p-2', 'mb-2', 'bg-body-tertiary');
    div.setAttribute('data-range-id', range.id);

    // Header: type badge | name | action buttons
    const header = document.createElement('div');
    header.classList.add('d-flex', 'align-items-center', 'gap-2', 'flex-wrap');

    const typeBadge = document.createElement('span');
    typeBadge.classList.add('badge', 'bg-secondary', 'text-white');
    typeBadge.textContent = range.type || '(no type)';
    header.appendChild(typeBadge);

    const nameSpan = document.createElement('span');
    nameSpan.classList.add('fw-semibold', 'flex-grow-1');
    nameSpan.textContent = range.name || range.id;
    header.appendChild(nameSpan);

    const actions = document.createElement('div');
    actions.classList.add('btn-group', 'btn-group-sm');

    actions.appendChild(makeBtn('+ Range', ['btn-outline-primary'],
        () => addChildRange(structmapId, range.id)));
    actions.appendChild(makeBtn('+ Files', ['btn-outline-success'],
        () => openFilePickerModal(structmapId, range.id)));
    actions.appendChild(makeBtn('Metadata', ['btn-outline-info'],
        () => openModsModalForRange(structmapId, range.id)));
    actions.appendChild(makeBtn('Edit', ['btn-outline-secondary'],
        () => openEditRangeModal(range.name ?? '', range.type ?? 'Item', ({ name, type }) => {
            range.name = name || null;
            range.type = type;
            renderLogicalStructMap(structmapId);
        })));

    if (!isRoot) {
        actions.appendChild(makeBtn('↑', ['btn-outline-secondary'],
            () => moveRange(structmapId, range.id, -1)));
        actions.appendChild(makeBtn('↓', ['btn-outline-secondary'],
            () => moveRange(structmapId, range.id, 1)));
        actions.appendChild(makeBtn('×', ['btn-outline-danger'],
            () => removeRange(structmapId, range.id)));
    }

    header.appendChild(actions);
    div.appendChild(header);

    // Metadata summary
    const metaSummary = buildMetadataSummary(range);
    if (metaSummary) div.appendChild(metaSummary);

    // Files list
    if (range.files && range.files.length > 0) {
        const filesList = document.createElement('ul');
        filesList.classList.add('list-unstyled', 'ms-2', 'mt-1', 'mb-0');
        for (const fp of range.files) {
            const li = document.createElement('li');
            li.classList.add('d-flex', 'align-items-center', 'gap-1', 'small', 'text-body-secondary');
            const removeBtn = makeBtn('×', ['btn-outline-danger', 'btn-sm', 'py-0', 'px-1'],
                () => removeFileFromRange(structmapId, range.id, fp.localPath));
            const pathSpan = document.createElement('span');
            pathSpan.textContent = fp.localPath;
            li.appendChild(removeBtn);
            li.appendChild(pathSpan);
            filesList.appendChild(li);
        }
        div.appendChild(filesList);
    }

    // Child ranges
    if (range.ranges && range.ranges.length > 0) {
        const childrenDiv = document.createElement('div');
        childrenDiv.classList.add('ms-3', 'mt-2');
        for (const child of range.ranges) {
            childrenDiv.appendChild(renderRange(child, structmapId, false));
        }
        div.appendChild(childrenDiv);
    }

    return div;
}

function buildMetadataSummary(range) {
    const parts = [];

    if (range.accessRestrictions && range.accessRestrictions.length > 0) {
        parts.push('Access: ' + range.accessRestrictions.join(', '));
    }
    if (range.rightsStatement) {
        const shortLabel = getRightsShortLabel(range.rightsStatement);
        parts.push('Rights: ' + (shortLabel || range.rightsStatement));
    }
    if (range.recordInfo && range.recordInfo.recordIdentifiers && range.recordInfo.recordIdentifiers.length > 0) {
        const ri = range.recordInfo.recordIdentifiers;
        const display = ri.length === 1
            ? `${ri[0].value} (${ri[0].source})`
            : `${ri.length} identifiers`;
        parts.push('ID: ' + display);
    }

    if (!parts.length) return null;

    const summary = document.createElement('div');
    summary.classList.add('small', 'text-body-secondary', 'mt-1');
    summary.textContent = parts.join(' · ');
    return summary;
}

function makeBtn(text, extraClasses, onClick) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.classList.add('btn', ...extraClasses);
    btn.textContent = text;
    btn.addEventListener('click', onClick);
    return btn;
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
        renderLogicalStructMap(structmapId);
    });
}

function removeRange(structmapId, id) {
    const root = logicalStructMapState[structmapId];
    const result = findParentAndIndex(root, id);
    if (!result) return;
    if (!confirm('Remove this range and all its children?')) return;
    result.parent.ranges.splice(result.index, 1);
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
    renderLogicalStructMap(structmapId);
}

function removeFileFromRange(structmapId, rangeId, localPath) {
    const root = logicalStructMapState[structmapId];
    const range = findRange(root, rangeId);
    if (!range) return;
    range.files = range.files.filter(f => f.localPath !== localPath);
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
    renderLogicalStructMap(structmapId);
}

// ----------------------------------------------------------------
// Edit range modal (name + type)
// ----------------------------------------------------------------

const RANGE_TYPES = ['Collection', 'Item'];

function createEditRangeModal() {
    const modal = document.createElement('div');
    modal.classList.add('modal', 'fade');
    modal.id = 'editRangeModal';
    modal.setAttribute('tabindex', '-1');
    modal.setAttribute('aria-hidden', 'true');
    const typeOptions = RANGE_TYPES.map(t => `<option value="${t}">${t}</option>`).join('');
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
                        <select class="form-select" id="editRangeType">${typeOptions}</select>
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
        bootstrap.Modal.getInstance(document.getElementById('editRangeModal')).hide();
        if (editRangeCallback) editRangeCallback({ name, type });
        editRangeCallback = null;
    });

    // Clear callback if modal is dismissed without saving
    document.getElementById('editRangeModal').addEventListener('hidden.bs.modal', () => {
        editRangeCallback = null;
    });
}

function openEditRangeModal(currentName, currentType, onConfirm) {
    editRangeCallback = onConfirm;
    document.getElementById('editRangeName').value = currentName;
    const typeSelect = document.getElementById('editRangeType');
    typeSelect.value = RANGE_TYPES.includes(currentType) ? currentType : RANGE_TYPES[0];
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
    launcher.setAttribute('data-access',
        (range?.accessRestrictions ?? []).join(','));
    launcher.setAttribute('data-rights',
        getRightsShortLabel(range?.rightsStatement) ?? '');
    launcher.setAttribute('data-recordinfo',
        toRecordInfoCompact(range?.recordInfo) ?? '');
    launcher.setAttribute('data-tool-output', '');

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
        const index = container.getAttribute('data-record-info-index');
        const sourceEl = document.getElementById(`recordInfoSourceSelect_${index}`);
        const valueEl = document.getElementById(`recordInfoValue_${index}`);
        if (sourceEl && valueEl && valueEl.value.trim()) {
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
    return null;
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
    modal.innerHTML = `
        <div class="modal-dialog modal-lg">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">
                        <svg class="bi"><use xlink:href="#file-earmark-plus"/></svg>
                        <span class="ms-2">Add files to range</span>
                    </h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <input type="text" id="filePickerSearch" class="form-control mb-2" placeholder="Filter files...">
                    <div id="filePickerList" style="max-height:400px;overflow-y:auto;"></div>
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

function openFilePickerModal(structmapId, rangeId) {
    addFilesTargetStructmapId = structmapId;
    addFilesTargetRangeId = rangeId;

    const list = document.getElementById('filePickerList');
    list.innerHTML = '';
    document.getElementById('filePickerSearch').value = '';

    const files = (typeof physicalFilePaths !== 'undefined' ? physicalFilePaths : []).filter(Boolean);
    const root = logicalStructMapState[structmapId];
    const range = root ? findRange(root, rangeId) : null;
    const alreadyAdded = new Set((range?.files || []).map(f => f.localPath));

    for (const path of files) {
        const item = document.createElement('div');
        item.classList.add('file-picker-item', 'form-check');
        const safeId = 'fp_' + path.replace(/[^a-zA-Z0-9]/g, '_');
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
