// --------------------------------------------------------------------------------------------
// Statements, derived values and the METS / IIIF preview (issue #433).
//
// Loaded only on an installation whose access condition profile has statements (for Leeds, the
// specific copyright statements), next to the _AccessStatementFields partial. A person chooses a
// restriction and a statement; each statement falls into a category (a matrix column) and names
// what it publishes in IIIF; derived values (for Leeds, the Access Tier) are looked up from
// restriction x category. The server writes the real values from each node's EFFECTIVE inputs on
// save; this file mirrors that so the consequences of a choice are visible before saving. The
// launcher carries what the node inherits, so clearing its own value shows what it will then have.
//
// Reads the page's accessConditionProfile (the API's profile) and rightsStatements.
// --------------------------------------------------------------------------------------------

const accessStatements = accessConditionProfile.statements;

// Mirror StatementProfile.NoneKey and .Wildcard.
const noneKey = '__none__';
const wildcardKey = '*';

// The node being edited: its own and inherited values, as the dialogue launcher carries them.
let statementNode = null;

// Called by deposit.js when the dialogue opens.
function applyAccessStatementFields(launcher) {
    const own = parseJsonOrEmpty(launcher.dataset.other);
    statementNode = {
        ownRestrictions: launcher.dataset.access ? launcher.dataset.access.split(',') : [],
        inheritedRestrictions: launcher.dataset.inhAccess ? launcher.dataset.inhAccess.split(',') : [],
        ownRights: launcher.dataset.rights ?? '',
        ownRightsSuppressed: launcher.dataset.rightsSuppressed === 'true',
        ownOther: own,
        inheritedOther: parseJsonOrEmpty(launcher.dataset.inhOther)
    };
    showOwnStatement(own[accessStatements.type] ?? null);
    updateStatementPreviews();
}

function parseJsonOrEmpty(text) {
    try { return text ? JSON.parse(text) : {}; } catch { return {}; }
}

function findStatement(value) {
    return accessStatements.values.find(v => v.value === value) ?? null;
}

function chosenStatement() {
    return document.getElementById("accessStatementSelect")?.value || null;
}

function chosenRestrictions() {
    const select = document.getElementById("accessRestrictionsSelect");
    if (!select) return null;
    return Array.from(select.selectedOptions).map(o => o.value).filter(v => v !== '');
}

// Select the node's own statement, or "(inherit from parent)". A value the profile does not know
// (from another source, or since removed) is shown as itself.
function showOwnStatement(value) {
    const select = document.getElementById("accessStatementSelect");
    if (!select) {
        const readOnly = document.getElementById("nonEditableAccessStatement");
        if (readOnly) readOnly.textContent = value ? (findStatement(value)?.label ?? value) : "(none)";
        return;
    }
    if (value && !Array.from(select.options).some(o => o.value === value)) {
        select.appendChild(new Option(`${value} (not in this profile)`, value));
    }
    select.value = value ?? "";
    showStatementDescription();
}

function showStatementDescription() {
    const target = document.getElementById("accessStatementDescription");
    if (target) target.textContent = findStatement(chosenStatement())?.description ?? "";
}

// ---- What the node will have after saving ---------------------------------------------------

// The node's effective statement: the chosen one, else what it inherits (null for none).
function effectiveStatement(node) {
    const chosen = chosenStatement();
    if (chosen) return { value: chosen, inherited: false };
    return { value: node.inheritedOther[accessStatements.type] ?? null, inherited: true };
}

// The matrix rows: the chosen restriction(s), else the inherited ones, else the "none" row.
function effectiveRestrictions(node) {
    const chosen = chosenRestrictions() ?? [];
    if (chosen.length > 0) return chosen;
    return node.inheritedRestrictions.length > 0 ? node.inheritedRestrictions : [noneKey];
}

// Mirrors StatementProfile.ColumnFor.
function columnFor(statement) {
    if (statement === null) return noneKey;
    return findStatement(statement)?.category ?? wildcardKey;
}

// Mirrors DerivedAccessCondition.Lookup.
function lookupCell(derived, row, column) {
    const cells = derived.matrix[row] ?? derived.matrix[wildcardKey];
    if (!cells) return null;
    return cells[column] ?? cells[wildcardKey] ?? null;
}

// Mirrors StatementProfile.ResolveDerived: the most restrictive value over the rows.
// Returns type -> { value, label } | { none: true }.
function derivedValues(node) {
    const result = {};
    const rows = effectiveRestrictions(node);
    const column = columnFor(effectiveStatement(node).value);
    for (const derived of accessStatements.derived) {
        const values = rows.map(row => lookupCell(derived, row, column)).filter(v => v !== null);
        const rank = value => derived.values.findIndex(v => v.value === value);
        const best = values.reduce((a, b) => (a === null || rank(b) > rank(a) ? b : a), null);
        result[derived.type] = best === null
            ? { none: true }
            : { value: best, label: derived.values.find(v => v.value === best)?.label ?? best };
    }
    return result;
}

// ---- Rendering ---------------------------------------------------------------------------
// Labels and text come from configuration, which is trusted, but nothing here is parsed as HTML.

function updateStatementPreviews() {
    if (!statementNode) return;
    const derived = derivedValues(statementNode);
    showDerivedValues(derived);
    showMetsPreview(statementNode, derived);
    showIiifPreview(statementNode);
}

function showDerivedValues(derived) {
    for (const target of document.querySelectorAll(".derived-access-condition")) {
        const v = derived[target.dataset.derivedType];
        const el = document.createElement(v.none ? "em" : "strong");
        el.textContent = v.none ? "(no value for this combination)" : `${v.label} (computed)`;
        target.replaceChildren(el);
    }
}

// One element as it will appear in METS; a note about it goes above it as an XML comment.
// Each line is { text, note }: notes are shown muted.
function accessConditionLines(type, value, note) {
    const element = { text: `<mods:accessCondition type="${type}">${value}</mods:accessCondition>`, note: false };
    return note ? [{ text: `<!-- ${note} -->`, note: true }, element] : [element];
}

function previewLine(line) {
    const div = document.createElement("div");
    if (line.note) div.className = "access-preview-note";
    div.textContent = line.text;
    return div;
}

// What this node's MODS will carry after saving: the elements chosen in the form; whatever else it
// already has, kept as it is; and the derived values, which the writer puts on every node with a
// chosen element of its own.
function showMetsPreview(node, derived) {
    const target = document.getElementById("accessPreviewMets");
    if (!target) return;
    const chosen = chosenElementLines(node);
    const hasOwnInput = chosen.length > 0 || hasOwnRights(node);
    const lines = [...chosen, ...keptElementLines(node), ...(hasOwnInput ? derivedElementLines(derived) : [])];
    const shown = lines.length > 0
        ? lines
        : [{ text: "<!-- nothing of its own: everything is inherited from the folder or range above -->", note: true }];
    target.replaceChildren(...shown.map(previewLine));
}

function chosenElementLines(node) {
    const restrictions = chosenRestrictions() ?? node.ownRestrictions;
    const lines = restrictions.flatMap(r => accessConditionLines("restriction on access", r));
    const statement = chosenStatement();
    if (statement) lines.push(...accessConditionLines(accessStatements.type, statement));
    return lines;
}

function hasOwnRights(node) {
    return !!node.ownRights || node.ownRightsSuppressed;
}

// Elements the node already has that this form does not edit.
function keptElementLines(node) {
    const kept = "already here, not edited by this form: kept";
    const lines = [];
    if (hasOwnRights(node)) {
        const uri = node.ownRightsSuppressed ? "" : (rightsStatements[node.ownRights]?.value ?? node.ownRights);
        lines.push(...accessConditionLines("use and reproduction", uri, kept));
    }
    const derivedTypes = new Set(accessStatements.derived.map(d => d.type));
    for (const [type, value] of Object.entries(node.ownOther)) {
        if (type !== accessStatements.type && !derivedTypes.has(type)) {
            lines.push(...accessConditionLines(type, value, kept));
        }
    }
    return lines;
}

function derivedElementLines(derived) {
    return Object.entries(derived)
        .filter(([, v]) => !v.none)
        .flatMap(([type, v]) => accessConditionLines(type, v.value, "computed"));
}

// What a IIIF manifest built from this profile says for the node: its effective statement's
// configured rights URI and requiredStatement. The statement is shown only when it is inherited;
// a statement chosen here is already in the dropdown above.
function showIiifPreview(node) {
    const list = document.getElementById("accessPreviewIiif");
    if (!list) return;
    list.replaceChildren();
    const statement = effectiveStatement(node);
    if (statement.value === null) {
        addDefinition(list, "Statement", "none here or above, so neither rights nor requiredStatement");
        return;
    }
    const found = findStatement(statement.value);
    if (statement.inherited) {
        addDefinition(list, "Statement", `${found?.label ?? statement.value} (inherited)`);
    }
    const entry = accessStatements.iiif?.entries.find(e => e.id === found?.iiif);
    if (!entry) {
        addDefinition(list, "rights", "nothing configured for this statement");
        return;
    }
    if (entry.rights) {
        addDefinition(list, "rights", rightsLabelFor(entry.rights), entry.rights);
    } else {
        addDefinition(list, "rights", "(none)");
    }
    if (entry.requiredStatement) {
        const label = entry.requiredStatementLabel ?? accessStatements.iiif.requiredStatementLabel;
        addDefinition(list, "requiredStatement", entry.requiredStatement, null, label);
    } else {
        addDefinition(list, "requiredStatement", "(none)");
    }
}

// One row of the IIIF preview: a term, then its text - a link when there is an href, and prefixed
// with a bold label when the IIIF property has one (requiredStatement's label and value).
function addDefinition(list, term, text, href = null, label = null) {
    const dt = document.createElement("dt");
    dt.className = "col-sm-4 fw-normal text-secondary";
    dt.textContent = term;
    const dd = document.createElement("dd");
    dd.className = "col-sm-8 mb-1";
    if (label) {
        const strong = document.createElement("strong");
        strong.textContent = `${label}: `;
        dd.append(strong);
    }
    if (href) {
        const link = document.createElement("a");
        link.href = href;
        link.target = "_blank";
        link.rel = "noopener";
        link.textContent = href;
        dd.append(link);
        if (text) dd.append(` (${text})`);
    } else {
        dd.append(text);
    }
    list.append(dt, dd);
}

function rightsLabelFor(uri) {
    return Object.values(rightsStatements).find(rs => rs.value === uri)?.label ?? null;
}

document.getElementById("accessRestrictionsSelect")?.addEventListener('change', updateStatementPreviews);
document.getElementById("accessStatementSelect")?.addEventListener('change', () => {
    showStatementDescription();
    updateStatementPreviews();
});
