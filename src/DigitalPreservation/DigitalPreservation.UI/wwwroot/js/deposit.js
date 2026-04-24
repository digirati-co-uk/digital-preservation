// --------------------------------------------------------------
// ------------------- RIGHTS, ACCESS AND RECORDINFO MODAL (mods)// 
// -------------------------------------------------------------- 

function populateModsModalFromAttributes(launcher){

    let accessRestrictionsSelect = document.getElementById("accessRestrictionsSelect");
    let nonEditableAccessRestrictions = document.getElementById("nonEditableAccessRestrictions");
    let rightsStatementSelect = document.getElementById("rightsStatementSelect");
    let nonEditableRightsStatement = document.getElementById("nonEditableRightsStatement");
    let recordInfoDynamicForm = document.getElementById("recordInfoDynamicForm");
    let nonEditableRecordInfo = document.getElementById("nonEditableRecordInfo");
    // and clear recordInfo form

    let accessConditionList = launcher.dataset.access;
    let rightsStatement = launcher.dataset.rights;
    let recordInfoCompact = launcher.dataset.recordinfo;
    let base64ToolOutput = launcher.dataset.toolOutput;
    document.getElementById("modsLocalPathInfo").innerHTML = modsContext.value;
    // document.getElementById("accessRestrictionsHelp").innerHTML = "Set access restriction(s) on " + modsContextPath;
    // document.getElementById("rightsStatementHelp").innerHTML = "Set rights statement on " + modsContextPath;
    // document.getElementById("recordInfoHelp").innerHTML = "Set record identifiers on " + modsContextPath;

    let toolOutputLink = document.getElementById("toolOutputLink");
    // remove event listeners
    toolOutputLink.innerHTML = "";
    if(base64ToolOutput){
        let windowOpener = document.createElement('a');
        toolOutputLink.appendChild(windowOpener);
        windowOpener.addEventListener("click", () => openHtmlStringInNewTab(base64ToolOutput));
    }

    if(accessRestrictionsSelect){
        accessRestrictionsSelect.selectedIndex = -1;
        if(accessConditionList.length > 0){
            let accessConditions = accessConditionList.split(",");
            for (const option of accessRestrictionsSelect.options) {
                if(accessConditions.includes(option.value)){
                    option.selected = true;
                }
            }
        }
    } else if (nonEditableAccessRestrictions){
        if(accessConditionList.length > 0){
            nonEditableAccessRestrictions.innerHTML = "";
            for (const ac of accessConditionList.split(",")) {
                let acLi = document.createElement("li");
                acLi.innerHTML = `<strong>${ac}</strong>`;
                nonEditableAccessRestrictions.appendChild(acLi);
            }
        } else {
            nonEditableAccessRestrictions.innerHTML = "<li><em>no explicit access restrictions</em></li>";
        }
    }

    if(rightsStatementSelect){
        rightsStatementSelect.selectedIndex = -1;
        if(rightsStatement){
            let rsUri = rightsStatements[rightsStatement].value;
            for (const option of rightsStatementSelect.options) {
                if(option.value === rsUri){
                    option.selected = true;
                }
            }
        }
    } else if (nonEditableRightsStatement){
        if(rightsStatement){
            nonEditableRightsStatement.innerHTML = `<strong>${rightsStatement}</strong><br/>${rightsStatements[rightsStatement].value}`;
        } else {
            nonEditableRightsStatement.innerHTML = "<ul><li><em>no explicit rights</em></li></ul>";
        }
    }

    if(recordInfoDynamicForm){
        recordInfoDynamicForm.innerHTML = "";
        if(recordInfoCompact){
            let recordIdentifiers = recordInfoFromCompactString(recordInfoCompact);
            for(let i = 0; i<recordIdentifiers.length; i++){
                const recordIdentifier = recordIdentifiers[i];
                let riEl = createRecordIdentifierElement(i, recordIdentifier);
                recordInfoDynamicForm.appendChild(riEl);
                document.getElementById(`recordInfoDelete_${i}`).addEventListener("click", (event) => {
                    let riDiv = event.target.closest("div");
                    riDiv.parentElement.removeChild(riDiv);
                });
            }
        }
    } else if (nonEditableRecordInfo){
        nonEditableRecordInfo.innerHTML = "<li><em>no explicit record info</em></li>";
        if(recordInfoCompact){
            let recordIdentifiers = recordInfoFromCompactString(recordInfoCompact);
            for(const recordIdentifier of recordIdentifiers){
                let riLi = document.createElement("li");
                riLi.innerText = `<strong>${recordIdentifier.source}:</strong> ${recordIdentifier.value}`;
                nonEditableRecordInfo.appendChild(riLi);
            }
            nonEditableRecordInfo.innerHTML = recordInfoCompact;
        }
    }

    // File links section — only shown for files
    const isFile = launcher.dataset.isFile === 'true';
    const fileLinksSection = document.getElementById('fileLinksSection');
    if (fileLinksSection) {
        fileLinksSection.style.display = isFile ? '' : 'none';
        if (isFile) {
            let links = [];
            try { links = JSON.parse(launcher.dataset.links || '[]'); } catch {}
            populateFileLinksList(links);
            populateFileLinkTargetSelect();
        }
    }
}

// ----------------------------------------------------------------
// File links modal state
// ----------------------------------------------------------------

let currentFileLinks = [];

function populateFileLinksList(links) {
    currentFileLinks = links.map(l => ({ to: l.to, role: l.role }));
    renderFileLinksList();
    refreshFileLinkRoleSelect();
}

function renderFileLinksList() {
    const list = document.getElementById('fileLinksList');
    const nonEditable = document.getElementById('nonEditableFileLinks');

    if (list) {
        list.innerHTML = '';
        currentFileLinks.forEach((link, i) => {
            const roleLabel = getRoleLabelFromUri(link.role) || link.role || '(unknown role)';
            const item = document.createElement('div');
            item.classList.add('input-group', 'mb-1', 'small');
            item.innerHTML = `<span class="input-group-text">${roleLabel}</span>
                              <span class="form-control">${link.to}</span>
                              <button type="button" class="btn btn-outline-danger" data-link-index="${i}">Remove</button>`;
            item.querySelector('button').addEventListener('click', () => {
                currentFileLinks.splice(i, 1);
                renderFileLinksList();
                refreshFileLinkRoleSelect();
            });
            list.appendChild(item);
        });
    } else if (nonEditable) {
        if (currentFileLinks.length === 0) {
            nonEditable.innerHTML = '<li><em>no file links</em></li>';
        } else {
            nonEditable.innerHTML = '';
            for (const link of currentFileLinks) {
                const li = document.createElement('li');
                li.textContent = `${getRoleLabelFromUri(link.role) || link.role}: ${link.to}`;
                nonEditable.appendChild(li);
            }
        }
    }
}

function refreshFileLinkRoleSelect() {
    const roleSelect = document.getElementById('fileLinkRoleSelect');
    const addBtn = document.getElementById('fileLinkAddBtn');
    if (!roleSelect) return;
    const usedRoles = new Set(currentFileLinks.map(l => l.role));
    roleSelect.innerHTML = '';
    let hasOptions = false;
    for (const [keyword, uri] of Object.entries(typeof fileLinkRoles !== 'undefined' ? fileLinkRoles : {})) {
        if (!usedRoles.has(uri)) {
            const opt = document.createElement('option');
            opt.value = uri;
            opt.textContent = keyword;
            roleSelect.appendChild(opt);
            hasOptions = true;
        }
    }
    if (addBtn) addBtn.disabled = !hasOptions;
}

function populateFileLinkTargetSelect() {
    const sel = document.getElementById('fileLinkTargetSelect');
    if (!sel) return;
    const currentPath = modsContext?.value ?? '';
    sel.innerHTML = '';
    const files = typeof physicalFilePaths !== 'undefined' ? physicalFilePaths : [];
    for (const path of files) {
        if (!path || path === currentPath) continue;
        const opt = document.createElement('option');
        opt.value = path;
        opt.textContent = path;
        sel.appendChild(opt);
    }
}

function getRoleLabelFromUri(uri) {
    if (!uri || typeof fileLinkRoles === 'undefined') return null;
    for (const [keyword, u] of Object.entries(fileLinkRoles)) {
        if (u === uri) return keyword;
    }
    return null;
}

const fileLinkAddBtn = document.getElementById('fileLinkAddBtn');
if (fileLinkAddBtn) {
    fileLinkAddBtn.addEventListener('click', () => {
        const roleSelect = document.getElementById('fileLinkRoleSelect');
        const targetSelect = document.getElementById('fileLinkTargetSelect');
        const role = roleSelect?.value;
        const to = targetSelect?.value;
        if (!role || !to) return;
        if (currentFileLinks.some(l => l.role === role)) return; // duplicate role guard
        currentFileLinks.push({ to, role });
        renderFileLinksList();
        refreshFileLinkRoleSelect();
    });
}

// Serialize currentFileLinks to hidden field before modsModal form submits
const modsForm = document.querySelector('#modsModal form');
if (modsForm) {
    modsForm.addEventListener('submit', () => {
        const hiddenField = document.getElementById('fileLinksJson');
        if (hiddenField && document.getElementById('fileLinksSection')?.style.display !== 'none') {
            hiddenField.value = JSON.stringify(currentFileLinks);
        }
    });
}

function createRecordIdentifierElement(index, recordIdentifier){
    let s1 = `<div class="input-group mb-3 record-info-container" id="recordIdentifiers_${index}" data-record-info-index="${index}">
                        <input type="hidden" name="RecordIdentifiers.index" value="${index}"/>
                        <span class="input-group-text">Source</span>
                        <select id="recordInfoSourceSelect_${index}" class="form-select" name="RecordIdentifiers[${index}].Source">`
    let s2 = "";
    for(const source of recordInfoSources){
        s2 += `<option value="${source}"${ source === recordIdentifier.source ? " selected" : ""}>${source}</option>`
    }
    let s3 = `</select>
                        <span class="input-group-text">Value</span>
                        <input id="recordInfoValue_${index}" type="text" class="form-control" 
                            name="RecordIdentifiers[${index}].Value" value="${recordIdentifier.value}">
                        <button id="recordInfoDelete_${index}" data-recordinfo-div="recordIdentifiers_${index}" 
                            class="btn btn-outline-primary" type="button">Delete</button>
                    </div>`;
    let e = document.createElement("div");
    e.innerHTML = s1 + s2 + s3;
    return e.firstChild;
}

// Script to wire up "Add another" record identifier button
let recordInfoAddAnother = document.getElementById("recordInfoAddAnother");
if(recordInfoAddAnother) {
    recordInfoAddAnother.addEventListener("click", () => {
        let highestIndex = -1;
        const riDivs = document.getElementsByClassName("record-info-container");
        for(let i = 0; i < riDivs.length; i++){
            const riDiv = riDivs[i];
            const riIndex = Number(riDiv.dataset.recordInfoIndex);
            if(riIndex > highestIndex){
                highestIndex = riIndex;
            }
        }
        let riEl = createRecordIdentifierElement(highestIndex + 1, {"source": null, "value": ""});
        document.getElementById("recordInfoDynamicForm").appendChild(riEl);
        document.getElementById(`recordInfoDelete_${highestIndex + 1}`).addEventListener("click", (event) => {
            let riDiv = event.target.closest("div");
            riDiv.parentElement.removeChild(riDiv);
        });
    });    
}


const recordInfoSources = [ "EMu", "Identity Service" ]; // TODO: from config
const rows = document.getElementsByClassName("deposit-row");
const newFolderContext = document.getElementById("newFolderContext");
const newFolderContextIsFile = document.getElementById("newFolderContextIsFile");
const newFileContext = document.getElementById("newFileContext");
const newFileContextIsFile = document.getElementById("newFileContextIsFile");
const modsContext = document.getElementById("modsContext");
const modsContextIsFile = document.getElementById("modsContextIsFile");

const deleteButton = document.getElementById("deleteButton");
const deleteFromDepositRadio = document.getElementById("deleteFromDeposit");
const deleteFromMetsAndDepositRadio = document.getElementById("deleteFromMetsAndDeposit");
const deleteItemHelp = document.getElementById("deleteItemHelp");

const deleteDepositButton = document.getElementById("deleteDepositButton");
const confirmDepositDelete = document.getElementById("confirmDepositDelete");

const addToMetsButton = document.getElementById("addToMetsButton");
const addToMetsHelp = document.getElementById("addToMetsHelp");

document.getElementById("toggleRhs").addEventListener("click", () => {
    const sideState = localStorage.getItem("sidebarInfoPanel");
    if (sideState === "hide"){
        localStorage.setItem("sidebarInfoPanel", "show");
    } else {
        localStorage.setItem("sidebarInfoPanel", "hide");
    }
});

document.getElementById("selectNonMets").addEventListener("click", () => {
    const itemSelectors = document.getElementsByClassName("item-selector");
    for(const itemSelector of itemSelectors)
    {
        const row = itemSelector.closest("tr");
        if (row.dataset.whereabouts === "Deposit"){
            itemSelector.checked = true;
        }
        if (row.dataset.mismatches?.toLowerCase() === "true"){
            itemSelector.checked = true;
        }
    }
});

deleteFromDepositRadio.addEventListener("change", () => {
    deleteButton.removeAttribute("disabled");
});

deleteFromMetsAndDepositRadio.addEventListener("change", () => {
    deleteButton.removeAttribute("disabled");
});

document.getElementById("deleteSelection").addEventListener("click", () => {

    deleteFromDepositRadio.checked = false;
    deleteFromMetsAndDepositRadio.checked = false;
    deleteButton.setAttribute("disabled", "true");
    deleteFromDepositRadio.setAttribute("disabled", "true");
    deleteFromMetsAndDepositRadio.setAttribute("disabled", "true");

    const itemSelectors = document.getElementsByClassName("item-selector");
    const deleteSelectionObject = { items: [] };
    let summary = "<table>";
    let metadataCount = 0;
    for(const itemSelector of itemSelectors)
    {
        if (itemSelector.checked){
            const row = itemSelector.closest("tr");
            const type = row.dataset.type;
            const relativePath = row.dataset.path;
            const isMetadata = row.dataset.metadata.toLowerCase() === "true";
            if(isMetadata){
                metadataCount++;
                continue;
            }
            if (relativePath && (type === "directory" || type === "file")){
                const whereabouts = row.dataset.whereabouts;
                const item = {
                    path: relativePath,
                    isDir: (type === "directory"),
                    where: whereabouts
                };
                deleteSelectionObject.items.push(item);
                summary += "<tr><td>";
                summary += whereabouts;
                summary += " - </td><td>";
                summary += item.isDir ? "🗀" : "🗎";
                summary += "</td><td>";
                summary += relativePath;
                summary += "</td></tr>";
            }
        }
    }
    summary += "</table>";

    if(metadataCount > 0)
    {
        deleteItemHelp.innerHTML = `<div class="alert alert-danger" role="alert"><p>${metadataCount} item(s) are in the metadata folder, these cannot be deleted.</p></div>`;
        document.getElementById("deleteSelectionObject").value = "";
    }
    else
    {
        if (deleteSelectionObject.items.length > 0){
            deleteItemHelp.innerHTML = summary;
            deleteFromMetsAndDepositRadio.removeAttribute("disabled");
            if (archivalGroupExists){
                deleteFromDepositRadio.removeAttribute("disabled");
            }
        } else {
            deleteItemHelp.innerHTML = "<p>There are no items selected.</p>"
        }
        document.getElementById("deleteSelectionObject").value = JSON.stringify(deleteSelectionObject);
    }
});


document.getElementById("addSelectionToMets").addEventListener("click", () => {

    addToMetsButton.setAttribute("disabled", "true");

    const itemSelectors = document.getElementsByClassName("item-selector");
    const addToMetsObject = [];
    let summary = "<table>";
    for(const itemSelector of itemSelectors)
    {
        if (itemSelector.checked){
            const row = itemSelector.closest("tr");
            const type = row.dataset.type;
            const relativePath = row.dataset.path;
            if (relativePath && (type === "directory" || type === "file")){
                const whereabouts = row.dataset.whereabouts;
                if(whereabouts === "Deposit" || whereabouts === "Both"){
                    const item = {
                        path: relativePath,
                        isDir: (type === "directory"),
                        where: whereabouts
                    };
                    addToMetsObject.push(item);
                    summary += "<tr><td>";
                    summary += item.isDir ? "🗀" : "🗎";
                    summary += " - </td><td>";
                    summary += relativePath;
                    summary += "</td></tr>";
                }
            }
        }
    }
    summary += "</table>";
    document.getElementById("addToMetsObject").value = JSON.stringify(addToMetsObject);
    if (addToMetsObject.length > 0){
        addToMetsHelp.innerHTML = summary;
        addToMetsButton.removeAttribute("disabled");
    } else {
        addToMetsHelp.innerHTML = "<p>There are no items selected.</p>"
    }
});



document.getElementById("deleteDepositModal").addEventListener("show.bs.modal", () => {
    confirmDepositDelete.checked = false;
    deleteDepositButton.disabled = "disabled";
});

confirmDepositDelete.addEventListener("change", () => {
    if (confirmDepositDelete.checked){
        deleteDepositButton.removeAttribute("disabled");
    } else {
        deleteDepositButton.disabled = "disabled";
    }
});

let clicked = false;
for (const row of rows) {
    const path = row.dataset.path;
    if (!(path === "objects" || path.startsWith("objects/")))
    {
        continue;
    }
    row.addEventListener('click', (event) => {
        const row = event.currentTarget;
        const path = row.dataset.path;
        console.log("Click event on row for path: " + path);
        const isFile = row.dataset.type === "file";
        newFileContext.value = path;
        newFolderContext.value = path;
        modsContext.value = path;
        newFolderContextIsFile.value = isFile;
        newFileContextIsFile.value = isFile;
        modsContextIsFile.value = isFile;

        let modsLauncher = event.target.closest('a');
        if(modsLauncher && modsLauncher.classList.contains("mods-launcher")){
            populateModsModalFromAttributes(modsLauncher);
        }

        if (row.classList.contains("table-active")){
            for (const r of rows) { r.classList.remove("table-active"); }
            clicked = false;
        } else {
            for (const r of rows) { r.classList.remove("table-active"); }
            row.classList.add("table-active");
            clicked = true;
        }
    });
}

const COMPACT_DELIMITER = "-|-";

// Matches "value(source)" — the source is always in the final set of parens
const RECORD_IDENTIFIER_RE = /^(.*)\(([^)]*)\)$/;

/**
 * @param {string} compactString
 * @param {string} [delimiter]
 * @returns {{ source: string, value: string }[] | null}
 */
function recordInfoFromCompactString(compactString, delimiter = COMPACT_DELIMITER) {
    if (!compactString || !compactString.trim()) {
        return null;
    }

    const identifiers = [];
    for (const part of compactString.split(delimiter)) {
        const match = RECORD_IDENTIFIER_RE.exec(part);
        if (match) {
            identifiers.push({ value: match[1], source: match[2] });
        }
    }

    return identifiers;
}


// --------------------------------------------------------------
// ---------------script for Upload File modal id=uploadFileModal
// --------------------------------------------------------------

const fileSelector = document.getElementById('depositFile');
const checksum = document.getElementById('checksum');
const fileName = document.getElementById('depositFileName');
const fileContentType = document.getElementById('depositFileContentType');

fileSelector.addEventListener('change', () => {
    hashFile();
    fileName.value = fileSelector.files[0].name;
    fileContentType.value = fileSelector.files[0].type;
});

function hashFile() {
    readBinaryFile(fileSelector.files[0])
        .then(function (result) {
            result = new Uint8Array(result);
            return window.crypto.subtle.digest("SHA-256", result);
        })
        .then(function (result) {
            result = new Uint8Array(result);
            checksum.value = Uint8ArrayToHexString(result);
        });
}

function readBinaryFile(file) {
    return new Promise((resolve) => {
        const fr = new FileReader();
        fr.onload = () => {
            resolve(fr.result);
        };
        fr.readAsArrayBuffer(file);
    });
}

function Uint8ArrayToHexString(ui8array) {
    let hexString = "",
        h;
    for (let i = 0; i < ui8array.length; i++) {
        h = ui8array[i].toString(16);
        if (h.length === 1) {
            h = "0" + h;
        }
        hexString += h;
    }
    const p = Math.pow(2, Math.ceil(Math.log2(hexString.length)));
    hexString = hexString.padStart(p, "0");
    return hexString;
}
