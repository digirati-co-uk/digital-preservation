using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.XmlGen.Mets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DigitalPreservation.Common.Model.Mets;
public interface IMetadataManager
{
    AmdSecType? ProcessAllFileMetadata(ref FullMets fullMets, DivType? div, WorkingFile workingFile, string operationPath, bool newUpload = false);
}
