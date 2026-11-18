// --------------------------------------------------------------------------------------------
// Statements, derived values and the METS / IIIF preview (issue #433).
//
// Loaded only on an installation whose access condition profile has statements (for Leeds, the
// specific copyright statements), next to the _AccessStatementFields partial. A person chooses a
// restriction and a statement; each statement falls into a category (a matrix column) and names
// what it publishes in IIIF; derived values (for Leeds, the Access Tier) are looked up from
// restriction x category. The server writes the real values from each node's EFFECTIVE inputs on
// save; this file mirrors that so the consequences of a choice are visible before saving. Where an
// input will be inherited and the parent's value cannot be known here, it says so rather than guess.
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
        inheritedRestrictions: launcher.dataset.effAccess ? launcher.dataset.effAccess.split(',') : [],
        ownRights: launcher.dataset.rights ?? '',
        ownRightsSuppressed: launcher.dataset.rightsSuppressed === 'true',
        ownOther: own,
        inheritedOther: parseJsonOrEmpty(launcher.dataset.effOther)
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

// The node's effective statement: the chosen one, else what it inherits - unknown here if its own
// value has just been cleared, since what it will then inherit comes from the server.
function effectiveStatement(node) {
    const chosen = chosenStatement();
    if (chosen) return { value: chosen, known: true, inherited: false };
    if (node.ownOther[accessStatements.type]) return { value: undefined, known: false, inherited: true };
    return { value: node.inheritedOther[accessStatements.type] ?? null, known: true, inherited: true };
}

// The matrix rows: the chosen restriction(s), else the inherited ones - unknown if the node's own
// value has just been cleared.
function effectiveRestrictions(node) {
    const chosen = chosenRestrictions() ?? [];
    if (chosen.length > 0) return { rows: chosen, known: true };
    const rows = node.inheritedRestrictions.length > 0 ? node.inheritedRestrictions : [noneKey];
    return { rows, known: node.ownRestrictions.length === 0 };
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
// Returns type -> { value, label } | { unknown: true } | { none: true }.
function derivedValues(node) {
    const result = {};
    const restrictions = effectiveRestrictions(node);
    const statement = effectiveStatement(node);
    for (const derived of accessStatements.derived) {
        if (!restrictions.known || !statement.known) {
            result[derived.type] = { unknown: true };
            continue;
        }
        const column = columnFor(statement.value);
        const values = restrictions.rows.map(row => lookupCell(derived, row, column)).filter(v => v !== null);
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
        const el = document.createElement(v.unknown || v.none ? "em" : "strong");
        if (v.unknown) el.textContent = "(inherits; determined on save)";
        else if (v.none) el.textContent = "(no value for this combination)";
        else el.textContent = `${v.label} (computed)`;
        target.replaceChildren(el);
    }
}

function accessConditionLine(type, value, note) {
    const element = `<mods:accessCondition type="${type}">${value}</mods:accessCondition>`;
    return note ? `${element}   ${note}` : element;
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
    target.textContent = lines.length > 0
        ? lines.join("\n")
        : "Nothing of its own: everything is inherited from the folder or range above it.";
}

function chosenElementLines(node) {
    const restrictions = chosenRestrictions() ?? node.ownRestrictions;
    const lines = restrictions.map(r => accessConditionLine("restriction on access", r));
    const statement = chosenStatement();
    if (statement) lines.push(accessConditionLine(accessStatements.type, statement));
    return lines;
}

function hasOwnRights(node) {
    return !!node.ownRights || node.ownRightsSuppressed;
}

// Elements the node already has that this form does not edit.
function keptElementLines(node) {
    const kept = "(already here; kept)";
    const lines = [];
    if (hasOwnRights(node)) {
        const uri = node.ownRightsSuppressed ? "" : (rightsStatements[node.ownRights]?.value ?? node.ownRights);
        lines.push(accessConditionLine("use and reproduction", uri, kept));
    }
    const derivedTypes = new Set(accessStatements.derived.map(d => d.type));
    for (const [type, value] of Object.entries(node.ownOther)) {
        if (type !== accessStatements.type && !derivedTypes.has(type)) {
            lines.push(accessConditionLine(type, value, kept));
        }
    }
    return lines;
}

function derivedElementLines(derived) {
    return Object.entries(derived)
        .filter(([, v]) => !v.none)
        .map(([type, v]) => v.unknown
            ? accessConditionLine(type, "?", "(computed on save from what it inherits)")
            : accessConditionLine(type, v.value, "(computed)"));
}

// What a IIIF manifest built from this profile says for the node: its effective statement's
// configured rights URI and requiredStatement.
function showIiifPreview(node) {
    const list = document.getElementById("accessPreviewIiif");
    if (!list) return;
    list.replaceChildren();
    const statement = effectiveStatement(node);
    if (!statement.known) {
        addDefinition(list, "Statement", "inherited from the folder or range above; shown once saved");
        return;
    }
    if (statement.value === null) {
        addDefinition(list, "Statement", "none on this node or above it, so no rights or requiredStatement");
        return;
    }
    const found = findStatement(statement.value);
    addDefinition(list, "Statement", `${found?.label ?? statement.value}${statement.inherited ? " (inherited)" : ""}`);
    const entry = accessStatements.iiif?.entries.find(e => e.id === found?.iiif);
    if (!entry) {
        addDefinition(list, "rights / requiredStatement", "nothing configured for this statement");
        return;
    }
    if (entry.rights) addDefinition(list, "rights", rightsLabelFor(entry.rights), entry.rights);
    else addDefinition(list, "rights", "(none)");
    if (entry.requiredStatement) {
        const label = entry.requiredStatementLabel ?? accessStatements.iiif.requiredStatementLabel;
        addDefinition(list, `requiredStatement: "${label}"`, entry.requiredStatement);
    } else {
        addDefinition(list, "requiredStatement", "(none)");
    }
}

function addDefinition(list, term, text, href) {
    const dt = document.createElement("dt");
    dt.textContent = term;
    const dd = document.createElement("dd");
    if (href) {
        const link = document.createElement("a");
        link.href = href;
        link.target = "_blank";
        link.rel = "noopener";
        link.textContent = href;
        dd.append(link);
        if (text) dd.append(` (${text})`);
    } else {
        dd.textContent = text;
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
