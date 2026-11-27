// Tests for wwwroot/js/time-segments.js: the structure editor's warnings about a time segment and
// its file's duration (issue #508).
//
// Run from src/DigitalPreservation:  node --test DigitalPreservation.UI.Tests/js/*.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const root = fileURLToPath(new URL('../../', import.meta.url));
const context = vm.createContext({});
vm.runInContext(readFileSync(root + 'DigitalPreservation.UI/wwwroot/js/time-segments.js', 'utf8'), context);
const { timeSegmentWarning, fileDurationNote } = context;

const tape = { duration: 2641, entered: false };   // 44:01

test('a part within its tape has no warning', () => {
    assert.equal(timeSegmentWarning({ beginTime: 2283, endTime: 2597 }, tape), null);
});

test('a part that ends after its tape does is warned about', () => {
    // The Liddle slip: 38:03 - 46:17 on a 44:01 tape.
    assert.equal(timeSegmentWarning({ beginTime: 2283, endTime: 2777 }, tape), 'Ends after the file does (44:01)');
});

test('half a second over is rounding, not a mistake', () => {
    assert.equal(timeSegmentWarning({ beginTime: 0, endTime: 2641.4 }, tape), null);
});

test('an entered duration says so', () => {
    assert.equal(timeSegmentWarning({ beginTime: 0, endTime: 3000 }, { duration: 2700, entered: true }),
        'Ends after the file does (45:00, entered by hand)');
});

test('a timed part of a file with no duration cannot be checked', () => {
    assert.match(timeSegmentWarning({ beginTime: 0, endTime: 600 }, null), /no recorded duration/);
});

test('no times, or a file that is not time-based, means nothing to warn about', () => {
    assert.equal(timeSegmentWarning({ beginTime: null, endTime: null }, null), null);
    assert.equal(timeSegmentWarning({ beginTime: 0, endTime: 600 }, undefined), null);
});

test('the segment editor says how long the file is', () => {
    assert.equal(fileDurationNote(tape), 'The file is 44:01 long.');
    assert.equal(fileDurationNote({ duration: 3723, entered: true }), 'The file is 1:02:03 long (entered by hand).');
    assert.equal(fileDurationNote(null), 'This file has no recorded duration.');
    assert.equal(fileDurationNote(undefined), '');
});
