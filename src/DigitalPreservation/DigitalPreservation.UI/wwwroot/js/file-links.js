// file-links.js
// Renders → (forward) and ← (back) link arrows on physical file rows,
// and wires hover-highlighting between linked rows.

document.addEventListener('DOMContentLoaded', () => {
    if (typeof physicalFileLinksData === 'undefined') return;
    const reverseLinks = buildReverseLinks(physicalFileLinksData);
    decorateFileRows(reverseLinks);
});

// Build reverse index: targetPath → [{from, role}]
function buildReverseLinks(data) {
    const reverseLinks = {};
    for (const [fromPath, links] of Object.entries(data)) {
        for (const link of links) {
            if (!reverseLinks[link.to]) reverseLinks[link.to] = [];
            reverseLinks[link.to].push({ from: fromPath, role: link.role });
        }
    }
    return reverseLinks;
}

function decorateFileRows(reverseLinks) {
    const fileRows = document.querySelectorAll('tr[data-type="file"][data-path]');
    for (const row of fileRows) {
        const path = row.dataset.path;
        if (!path) continue;

        const safeId = 'frow-' + path.replaceAll(/[^a-zA-Z0-9]/g, '_');
        row.id = safeId;

        const forwardLinks = physicalFileLinksData[path] || [];
        const backLinks = reverseLinks[path] || [];
        if (!forwardLinks.length && !backLinks.length) continue;

        const linksTd = row.querySelector('td[aria-label="file-links"]');
        if (!linksTd) continue;

        for (const link of forwardLinks) {
            const targetSafeId = 'frow-' + link.to.replaceAll(/[^a-zA-Z0-9]/g, '_');
            const label = getRoleLabelFromUri(link.role) || link.role || '→';
            linksTd.appendChild(makeFileLinkArrow('→', targetSafeId, label, 'link-primary'));
        }

        for (const bl of backLinks) {
            const sourceSafeId = 'frow-' + bl.from.replaceAll(/[^a-zA-Z0-9]/g, '_');
            const label = getRoleLabelFromUri(bl.role) || bl.role || '←';
            linksTd.appendChild(makeFileLinkArrow('←', sourceSafeId, label + ' (linked from)', 'link-secondary'));
        }
    }
}

function makeFileLinkArrow(text, targetRowId, title, linkClass) {
    const a = document.createElement('a');
    a.href = '#' + targetRowId;
    a.classList.add('ms-1', 'text-decoration-none', linkClass);
    a.dataset.targetRow = targetRowId;
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
