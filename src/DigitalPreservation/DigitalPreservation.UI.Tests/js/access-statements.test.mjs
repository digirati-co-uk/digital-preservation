// Tests for wwwroot/js/access-statements.js, the metadata dialogue's tier and METS/IIIF previews on
// an installation with a statement profile (issue #433). The script mirrors C# (the matrix lookup
// and the rights written from a statement), so it is run here against the deployed Leeds profile
// and the same expected-tier table as the C# tests (leeds-tiers.json).
//
// Run from src/DigitalPreservation:  node --test DigitalPreservation.UI.Tests/js/*.test.mjs
// No packages: node:test, and a small stand-in for the few DOM calls the script makes.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const root = fileURLToPath(new URL('../../', import.meta.url));
const read = path => readFileSync(root + path, 'utf8');

const leeds = JSON.parse(read('Preservation.API/AccessConditionProfiles/leeds.json'));
const expectedTiers = JSON.parse(read('DigitalPreservation.Common.Model.Tests/AccessConditions/leeds-tiers.json'));
const script = read('DigitalPreservation.UI/wwwroot/js/access-statements.js');

const statementType = leeds.statements.type;
const tierLabel = value => leeds.statements.derived[0].values.find(v => v.value === value).label;

// ---- A stand-in for the DOM -------------------------------------------------------------------

class Element {
    constructor(tag) {
        this.tag = tag;
        this.children = [];
        this.textContent = '';
        this.className = '';
        this.dataset = {};
    }
    append(...children) { this.children.push(...children); }
    appendChild(child) { this.children.push(child); }
    replaceChildren(...children) { this.children = children; }
    addEventListener() { }
    // Everything the element shows, as text.
    get text() {
        return [this.textContent, ...this.children.map(c => (typeof c === 'string' ? c : c.text))].join('');
    }
}

class Select extends Element {
    constructor(values) {
        super('select');
        this.options = values.map(value => ({ value }));
        this.value = '';
    }
    get selectedOptions() { return this.value ? [{ value: this.value }] : []; }
    appendChild(option) { this.options.push(option); }
}

// The dialogue: editable (both dropdowns) or read-only (neither), opened on a node whose own and
// inherited values are as given. Returns what it shows.
function openDialogue({ editable = true, own = {}, inherited = {}, effectiveOther = {}, effectiveRights = '' } = {}) {
    const elements = {
        accessStatementDescription: new Element('div'),
        accessPreviewMets: new Element('div'),
        accessPreviewIiif: new Element('dl')
    };
    if (editable) {
        elements.accessRestrictionsSelect = new Select(['', ...leeds.restrictionAxis.values.map(v => v.value)]);
        elements.accessStatementSelect = new Select(['', ...leeds.statements.values.map(v => v.value)]);
    } else {
        elements.nonEditableAccessStatement = new Element('p');
    }
    const tier = new Element('p');
    tier.dataset.derivedType = 'tier';

    const document = {
        getElementById: id => elements[id] ?? null,
        querySelectorAll: selector => (selector === '.derived-access-condition' ? [tier] : []),
        createElement: tag => new Element(tag)
    };
    const context = vm.createContext({
        document,
        accessConditionProfile: leeds,
        rightsStatements: {
            PDM: { value: 'https://creativecommons.org/publicdomain/mark/1.0/', label: 'Public Domain Mark' },
            InC: { value: 'http://rightsstatements.org/vocab/InC/1.0/', label: 'In Copyright' }
        },
        Option: class { constructor(text, value) { this.text = text; this.value = value; } }
    });
    vm.runInContext(script, context);

    const ownOther = { ...(own.statement ? { [statementType]: own.statement } : {}), ...(own.other ?? {}) };
    const inheritedOther = { ...(inherited.statement ? { [statementType]: inherited.statement } : {}), ...(inherited.other ?? {}) };
    context.applyAccessStatementFields({
        dataset: {
            access: (own.restrictions ?? []).join(','),
            inhAccess: (inherited.restrictions ?? []).join(','),
            rights: own.rights ?? '',
            rightsSuppressed: 'false',
            effRights: own.rights ?? effectiveRights,
            other: JSON.stringify(ownOther),
            inhOther: JSON.stringify(inheritedOther),
            effOther: JSON.stringify(effectiveOther)
        }
    });

    return {
        // Choose in the dropdowns, as a person would; null chooses "(inherit from parent)".
        choose(restriction, statement) {
            elements.accessRestrictionsSelect.value = restriction ?? '';
            elements.accessStatementSelect.value = statement ?? '';
            context.updateStatementPreviews();
        },
        tier: () => tier.text,
        mets: () => elements.accessPreviewMets.children.map(line => line.textContent),
        iiif: () => elements.accessPreviewIiif.text
    };
}

const computed = value => `${tierLabel(value)} (computed)`;
const element = (type, value) => `<mods:accessCondition type="${type}">${value}</mods:accessCondition>`;

// ---- The matrix, cell by cell: the same table as AccessConditionProfileTests -------------------

// The tier the shared table gives; null for no restriction or statement, and values outside the
// vocabulary for unknown ones.
function expectedTier(pa, statement) {
    if (pa === 'PA1') {
        if (statement === null) return expectedTiers.pa1WithNoStatement;
        return expectedTiers.pa1TierByStatement[statement] ?? expectedTiers.pa1WithAnUnknownStatement;
    }
    if (pa === null) return expectedTiers.noRestriction;
    return expectedTiers.tierByOtherRestriction[pa] ?? expectedTiers.unknownRestriction;
}

test('every restriction and statement previews the agreed tier', () => {
    const restrictions = ['PA1', 'PA2', 'PA3', 'PA4', 'PA-unassessed', null, 'dlip-open'];
    const statements = [...Object.keys(expectedTiers.pa1TierByStatement), null, 'renamed-statement'];
    for (const pa of restrictions) {
        for (const statement of statements) {
            const dialogue = openDialogue();
            dialogue.choose(pa, statement);
            if (pa === null && statement === null) {
                // Nothing anywhere: the writer derives nothing, so neither does the preview.
                assert.match(dialogue.tier(), /no access conditions/, 'nothing chosen anywhere');
                continue;
            }
            assert.equal(dialogue.tier(), computed(expectedTier(pa, statement)), `${pa} x ${statement}`);
        }
    }
});

// ---- Own, inherited and cleared values -----------------------------------------------------------

test('a cleared value shows what the node inherits', () => {
    const dialogue = openDialogue({
        own: { restrictions: ['PA1'], statement: 'out-of-copyright' },
        inherited: { restrictions: ['PA4'], statement: 'inc-high-risk' }
    });
    dialogue.choose('PA1', 'out-of-copyright');
    assert.equal(dialogue.tier(), computed('0'));
    dialogue.choose(null, 'out-of-copyright');
    assert.equal(dialogue.tier(), computed('4'), 'the restriction cleared: PA4 is inherited');
    dialogue.choose('PA1', null);
    assert.equal(dialogue.tier(), computed('2'), 'the statement cleared: high risk is inherited');
    assert.match(dialogue.iiif(), /\(inherited\)/);
});

test('a read-only dialogue shows the node\'s values as stored, not only what it inherits', () => {
    const dialogue = openDialogue({
        editable: false,
        own: { restrictions: ['PA1'], statement: 'out-of-copyright', other: { tier: '0' } },
        inherited: { restrictions: ['PA4'], statement: 'inc-high-risk' },
        effectiveOther: { [statementType]: 'out-of-copyright', tier: '0' }
    });
    assert.equal(dialogue.tier(), tierLabel('0'), 'stored, so not "(computed)"');
    assert.ok(dialogue.mets().includes(element(statementType, 'out-of-copyright')));
    assert.ok(dialogue.mets().includes(element('tier', '0')));
    assert.doesNotMatch(dialogue.iiif(), /\(inherited\)/);
});

test('a read-only dialogue shows a tier another system wrote, without recomputing it', () => {
    // A Goobi export: a tier and nothing chosen. The platform never writes such a METS, so the
    // dialogue shows the tier as it is rather than what the matrix would give.
    const dialogue = openDialogue({
        editable: false,
        own: { other: { tier: '3' } },
        effectiveOther: { tier: '3' }
    });
    assert.equal(dialogue.tier(), tierLabel('3'));
    assert.ok(dialogue.mets().includes(element('tier', '3')));
    assert.match(dialogue.mets()[0], /as stored/);
});

test('a node with no access conditions anywhere previews no tier and writes none', () => {
    const dialogue = openDialogue();
    dialogue.choose(null, null);
    assert.match(dialogue.tier(), /no access conditions/);
    assert.ok(!dialogue.mets().some(line => line.includes('type="tier"')));
});

test('clearing a node\'s restriction and statement leaves it no inputs, though it had rights', () => {
    // The rights were written from the statement, so they go with it on save.
    const dialogue = openDialogue({ own: { restrictions: ['PA1'], statement: 'out-of-copyright', rights: 'PDM' } });
    dialogue.choose(null, null);
    assert.match(dialogue.tier(), /no access conditions/);
});

test('a rights statement above counts as an input, as it does to the writer', () => {
    // An ancestor with only a rights statement kept from before the profile: once saved it carries
    // the none x none tier, which descendants inherit; before then, its rights are inherited.
    const saved = openDialogue({ inherited: { other: { tier: '5' } } });
    saved.choose(null, null);
    assert.equal(saved.tier(), computed(expectedTiers.noRestriction));

    const notYetSaved = openDialogue({ effectiveRights: 'InC' });
    notYetSaved.choose(null, null);
    assert.equal(notYetSaved.tier(), computed(expectedTiers.noRestriction));
});

// ---- The rights statement written from the statement ---------------------------------------------

test('the rights statement follows the chosen statement and goes when it is cleared', () => {
    const orphan = leeds.statements.iiif.entries.find(e => e.id === leeds.statements.values
        .find(v => v.value === 'inc-low-risk-orphan-2039').iiif).rights;

    const dialogue = openDialogue({ own: { restrictions: ['PA1'], statement: 'out-of-copyright' } });
    dialogue.choose('PA1', 'inc-low-risk-orphan-2039');
    const lines = dialogue.mets();
    assert.ok(lines.includes(element('use and reproduction', orphan)));
    assert.ok(lines.includes('<!-- computed from the statement -->'));
    assert.ok(lines.includes(element('tier', '1')));

    dialogue.choose('PA1', null);
    assert.ok(!dialogue.mets().some(line => line.includes('use and reproduction')),
        'clearing the node\'s own statement removes the rights written from it');
});

// ---- A logical range's inherited values (logical-structmap.js) -----------------------------------

// Named functions taken from a page script alone, where the rest of the script drives the page.
function functionsFrom(path, names, globals = {}) {
    const source = read(path);
    // A function's source: from its declaration to the brace that closes its body.
    const take = name => {
        const start = source.indexOf(`function ${name}(`);
        assert.ok(start >= 0, `${name} is in ${path}`);
        let depth = 0;
        for (let i = source.indexOf('{', start); i < source.length; i++) {
            if (source[i] === '{') depth++;
            if (source[i] === '}' && --depth === 0) return source.slice(start, i + 1);
        }
        throw new Error(`${name} has no closing brace`);
    };
    const context = vm.createContext(globals);
    vm.runInContext(names.map(take).join('\n'), context);
    return context;
}

// Plain copies: objects made in a script's own realm don't deep-equal this module's.
const plain = value => JSON.parse(JSON.stringify(value));

const logicalStructMap = 'DigitalPreservation.UI/wwwroot/js/logical-structmap.js';

function rangeInheritance() {
    const context = functionsFrom(logicalStructMap, ['findParentAndIndex', 'ancestorsOf', 'inheritedForRange']);
    return (root, id) => plain(context.inheritedForRange(root, id));
}

test('a range inherits from the ranges above it as they stand in the browser', () => {
    const inheritedForRange = rangeInheritance();
    const child = { id: 'c', accessRestrictions: [], otherAccessConditions: null, ranges: [] };
    const middle = { id: 'm', accessRestrictions: [], otherAccessConditions: null, ranges: [child] };
    const root = {
        id: 'r', accessRestrictions: [], otherAccessConditions: null, ranges: [middle],
        // From the server: what the root inherits from the physical root.
        inheritedAccessRestrictions: ['PA1'],
        inheritedOtherAccessConditions: { [statementType]: 'out-of-copyright' }
    };
    assert.deepEqual(inheritedForRange(root, 'c'), { access: ['PA1'], rights: null, other: { [statementType]: 'out-of-copyright' } });

    // An unsaved edit to the middle range reaches the child at once.
    middle.accessRestrictions = ['PA4'];
    middle.otherAccessConditions = { [statementType]: 'inc-high-risk' };
    assert.deepEqual(inheritedForRange(root, 'c'), { access: ['PA4'], rights: null, other: { [statementType]: 'inc-high-risk' } });

    // Per type: a range that sets only a restriction passes its parent's statement down.
    middle.otherAccessConditions = null;
    assert.deepEqual(inheritedForRange(root, 'c'), { access: ['PA4'], rights: null, other: { [statementType]: 'out-of-copyright' } });
    assert.deepEqual(inheritedForRange(root, 'r'), { access: ['PA1'], rights: null, other: { [statementType]: 'out-of-copyright' } });
});

test('a range inherits the rights statement above it as it stands in the browser, not as last saved', () => {
    const inheritedForRange = rangeInheritance();
    const pdm = 'https://creativecommons.org/publicdomain/mark/1.0/';
    const child = { id: 'c', rightsStatement: null, effectiveRightsStatement: pdm, ranges: [] };
    const middle = { id: 'm', rightsStatement: pdm, effectiveRightsStatement: pdm, ranges: [child] };
    const root = { id: 'r', rightsStatement: null, inheritedRightsStatement: null, ranges: [middle] };
    assert.equal(inheritedForRange(root, 'c').rights, pdm);

    // Clearing the middle range's statement clears its rights. The child's effective rights, as
    // the server last gave them, still say otherwise.
    middle.rightsStatement = null;
    assert.equal(inheritedForRange(root, 'c').rights, null);

    // What the root inherits from the physical root reaches every range below.
    root.inheritedRightsStatement = pdm;
    assert.equal(inheritedForRange(root, 'c').rights, pdm);
    assert.equal(inheritedForRange(root, 'r').rights, pdm);
});

test('the root range inherits its effective rights statement only when it has none of its own', () => {
    const { rememberInheritedRights } = functionsFrom(logicalStructMap, ['rememberInheritedRights']);
    const pdm = 'https://creativecommons.org/publicdomain/mark/1.0/';
    const inheriting = { rightsStatement: null, effectiveRightsStatement: pdm };
    rememberInheritedRights(inheriting);
    assert.equal(inheriting.inheritedRightsStatement, pdm);

    const withItsOwn = { rightsStatement: pdm, effectiveRightsStatement: pdm };
    rememberInheritedRights(withItsOwn);
    assert.equal(withItsOwn.inheritedRightsStatement, null);
});

test('an edited range and the ranges below it lose their derived values until the next save', () => {
    const { dropDerivedConditions } = functionsFrom(logicalStructMap, ['dropDerivedConditions']);
    const child = { otherAccessConditions: { tier: '0' }, ranges: [] };
    const edited = {
        otherAccessConditions: { [statementType]: 'out-of-copyright', tier: '0', 'another type': 'kept' },
        ranges: [child]
    };
    dropDerivedConditions(edited, ['tier']);
    assert.deepEqual(plain(edited.otherAccessConditions), { [statementType]: 'out-of-copyright', 'another type': 'kept' });
    assert.equal(child.otherAccessConditions, null, 'a range left with nothing has no conditions, as when it never had any');
});

// ---- The restriction dropdown (deposit.js) --------------------------------------------------------

// A select as allowSeveralRestrictions sees it, with the single-select's "(inherit from parent)".
function restrictionSelect(multiple) {
    const inherit = { value: '', hidden: false, disabled: false };
    const help = { hidden: !multiple };
    const select = { multiple, dataset: {}, options: multiple ? [{ value: 'PA1' }] : [inherit, { value: 'PA1' }] };
    const { allowSeveralRestrictions } = functionsFrom('DigitalPreservation.UI/wwwroot/js/deposit.js',
        ['allowSeveralRestrictions'], { document: { getElementById: () => help } });
    return { select, inherit, help, allowSeveralRestrictions };
}

test('a node with several restrictions keeps them all where the profile offers one at a time', () => {
    const { select, inherit, help, allowSeveralRestrictions } = restrictionSelect(false);

    allowSeveralRestrictions(select, true);
    assert.equal(select.multiple, true, 'both of the node\'s restrictions can stay selected');
    assert.equal(inherit.disabled && inherit.hidden, true, 'with several, clearing is deselecting them all');
    assert.equal(help.hidden, false);

    // The next node opened has one restriction, or none: back to the profile's single choice.
    allowSeveralRestrictions(select, false);
    assert.equal(select.multiple, false);
    assert.equal(inherit.disabled || inherit.hidden, false);
    assert.equal(help.hidden, true);
});

test('a profile that allows several restrictions is left as it is', () => {
    const { select, help, allowSeveralRestrictions } = restrictionSelect(true);
    allowSeveralRestrictions(select, false);
    assert.equal(select.multiple, true);
    assert.equal(help.hidden, false);
});
