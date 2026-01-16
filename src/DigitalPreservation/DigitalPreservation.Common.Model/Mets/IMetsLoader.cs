using System.Xml.Linq;
using DigitalPreservation.Common.Model.Transit;

namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsLoader
{
    Task<Uri?> FindMetsFile(Uri root);
    Task<WorkingFile?> LoadMetsFileAsWorkingFile(Uri file);
    Task<(XDocument?, string)> ExamineXml(Uri file, string? digest, bool parse);
}