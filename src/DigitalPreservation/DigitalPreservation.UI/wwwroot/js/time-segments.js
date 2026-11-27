// --------------------------------------------------------------
// Time segments against their file's duration (issue #508)
// --------------------------------------------------------------
// Warnings, not refusals: the METS may know more than the platform measured. Kept apart from
// logical-structmap.js so it can be tested on its own (DigitalPreservation.UI.Tests/js).

// "1:02:03" or "44:01": how the structure editor shows times.
function formatClock(seconds) {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
    return `${m}:${String(s).padStart(2, '0')}`;
}

// What to warn about a file pointer's time segment, or null.
//   known: undefined for a file that isn't time-based (nothing to check),
//          null for a time-based file with no recorded duration,
//          { duration, entered } otherwise.
function timeSegmentWarning(fp, known) {
    if (fp.beginTime == null && fp.endTime == null) return null;
    if (known === undefined) return null;
    if (known === null) return "The file has no recorded duration, so this can't be checked";
    const last = Math.max(fp.beginTime ?? 0, fp.endTime ?? 0);
    // Half a second's grace: times in METS are often rounded.
    if (last > known.duration + 0.5) {
        return `Ends after the file does (${formatClock(known.duration)}${known.entered ? ', entered by hand' : ''})`;
    }
    return null;
}

// The note shown in the segment editor.
function fileDurationNote(known) {
    if (known === undefined) return '';
    if (known === null) return 'This file has no recorded duration.';
    return `The file is ${formatClock(known.duration)} long${known.entered ? ' (entered by hand)' : ''}.`;
}
