// file-links.js
// Renders → (forward) and ← (back) link arrows on physical file rows,
// and wires hover-highlighting between linked rows.

document.addEventListener('DOMContentLoaded', () => {
    if (typeof physicalFileLinksData === 'undefined') return;

    // Build reverse index: targetPath → [{from, role}]
    const reverseLinks = {};
    for (const [fromPath, links] of Object.entries(physicalFileLinksData)) {
        for (const link of links) {
            if (!reverseLinks[link.to]) reverseLinks[link.to] = [];
            reverseLinks[link.to].push({ from: fromPath, role: link.role });
        }
    }

    // Process each physical file row
    const fileRows = document.querySelectorAll('tr[data-type="file"][data-path]');
    for (const row of fileRows) {
        const path = row.getAttribute('data-path');
        if (!path) continue;

        // Assign a stable anchor ID
        const safeId = 'frow-' + path.replace(/[^a-zA-Z0-9]/g, '_');
        row.id = safeId;

        const forwardLinks = physicalFileLinksData[path] || [];
        const backLinks = reverseLinks[path] || [];
        if (!forwardLinks.length && !backLinks.length) continue;

        // Find the name <td> (the one with aria-label="name" inside it)
        const nameSpan = row.querySelector('[aria-label="name"]');
        const nameTd = nameSpan?.closest('td');
        if (!nameTd) continue;

        // Forward links (→)
        for (const link of forwardLinks) {
            const targetSafeId = 'frow-' + link.to.replace(/[^a-zA-Z0-9]/g, '_');
            const label = getRoleLabelFromUri(link.role) || link.role || '→';
            nameTd.appendChild(makeFileLinkArrow('→', targetSafeId, label, 'link-primary'));
        }

        // Back links (←)
        for (const bl of backLinks) {
            const sourceSafeId = 'frow-' + bl.from.replace(/[^a-zA-Z0-9]/g, '_');
            const label = getRoleLabelFromUri(bl.role) || bl.role || '←';
            nameTd.appendChild(makeFileLinkArrow('←', sourceSafeId, label + ' (linked from)', 'link-secondary'));
        }
    }
});

function makeFileLinkArrow(text, targetRowId, title, linkClass) {
    const a = document.createElement('a');
    a.href = '#' + targetRowId;
    a.classList.add('ms-1', 'text-decoration-none', linkClass);
    a.setAttribute('data-target-row', targetRowId);
    a.setAttribute('title', title);
    a.textContent = text;
    a.addEventListener('mouseenter', () => {
        document.getElementById(targetRowId)?.classList.add('table-warning');
    });
    a.addEventListener('mouseleave', () => {
        document.getElementById(targetRowId)?.classList.remove('table-warning');
    });
    return a;
}

// getRoleLabelFromUri is defined in deposit.js; guard in case load order differs
function getRoleLabelFromUri(uri) {
    if (!uri || typeof fileLinkRoles === 'undefined') return null;
    for (const [keyword, u] of Object.entries(fileLinkRoles)) {
        if (u === uri) return keyword;
    }
    return null;
}
