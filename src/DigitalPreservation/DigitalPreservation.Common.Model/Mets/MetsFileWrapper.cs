using System.Xml.Linq;
using DigitalPreservation.Common.Model.Transit;
using Microsoft.Extensions.Primitives;

namespace DigitalPreservation.Common.Model.Mets;

public class MetsFileWrapper
{
    // The title of the object the METS file describes
    public string? Name { get; set; }

    // The location of this METS file's parent directory; the METS file should be at its root
    public Uri? Root {  get; set; }

    // An entry describing the METS file itself, because it is not (typically) included in itself
    public WorkingFile? Self { get; set; }

    public WorkingDirectory? PhysicalStructure { get; set; }

    // A list of all the directories mentioned, with their names
    // public List<WorkingDirectory> ContainersX { get; set; } = [];

    // A list of all the files mentioned, with their names and hashes (digests)
    public List<WorkingFile> Files { get; set; } = [];
    public XDocument? XDocument { get; set; }
    public string? ETag { get; set; }
}