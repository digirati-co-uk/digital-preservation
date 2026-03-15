using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.XmlGen.Mets;

namespace DigitalPreservation.Common.Model.Mets;
public interface IMetadataManager
{
    Result ProcessAllFileMetadata(FullMets fullMets, DivType? div, WorkingFile workingFile, string operationPath, bool newUpload = false);
}
