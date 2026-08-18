using DigitalPreservation.XmlGen.Mets;

namespace DigitalPreservation.Mets;

/// <summary>
/// ID → element lookup for one METS document, so that resolving many divs against the same
/// document does not rescan the fileSec and amdSec collections for each one. Resolution used to
/// be a <c>FirstOrDefault</c> per div, which made both cache population (run on every load) and
/// the sibling scan behind an add quadratic in the size of the deposit.
/// </summary>
/// <remarks>
/// First-wins on a duplicate ID, matching the <c>FirstOrDefault</c> behaviour it replaces: a
/// document with duplicate IDs is malformed, and this class is not the place to complain about
/// it — the path cache reports its own diagnostics, and a duplicate ID is caught by the
/// integrity tests.
/// Build one per operation and discard it: it is a snapshot, and every mutation invalidates it.
/// </remarks>
internal sealed class MetsIdIndex
{
    private readonly Dictionary<string, FileType> filesById = [];
    private readonly Dictionary<string, AmdSecType> amdSecsById = [];

    internal MetsIdIndex(DigitalPreservation.XmlGen.Mets.Mets mets)
    {
        foreach (var file in (mets.FileSec?.FileGrp ?? []).SelectMany(fileGrp => fileGrp.File))
        {
            if (file.Id != null)
            {
                filesById.TryAdd(file.Id, file);
            }
        }

        foreach (var amdSec in mets.AmdSec)
        {
            if (amdSec.Id != null)
            {
                amdSecsById.TryAdd(amdSec.Id, amdSec);
            }
        }
    }

    internal FileType? FileById(string? id) =>
        id != null && filesById.TryGetValue(id, out var file) ? file : null;

    internal AmdSecType? AmdSecById(string? id) =>
        id != null && amdSecsById.TryGetValue(id, out var amdSec) ? amdSec : null;
}
