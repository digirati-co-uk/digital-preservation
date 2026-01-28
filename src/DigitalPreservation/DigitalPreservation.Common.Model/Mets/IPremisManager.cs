using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace DigitalPreservation.Common.Model.Mets;

public interface IPremisManager
{
    FileFormatMetadata? Read(PremisComplexType premis);
    FileFormatMetadata Read(File file);
    PremisComplexType Create(FileFormatMetadata premisFile, ExifMetadata? exifMetadata = null);
    void Patch(PremisComplexType premis, FileFormatMetadata premisFile, ExifMetadata? exifMetadata = null);
    string Serialise(PremisComplexType premis);
    XmlElement? GetXmlElement(PremisComplexType premis, bool fileElement);
    XmlElement? GetXmlElement(ExifTag exifMetdata, XmlDocument document);
}
