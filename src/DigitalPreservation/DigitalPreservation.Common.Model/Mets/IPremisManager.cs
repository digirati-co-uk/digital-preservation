using DigitalPreservation.XmlGen.Premis.V3;
using System.Xml;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace DigitalPreservation.Common.Model.Mets;

public interface IPremisManager<T>
    where T : class
{
    T? Read(PremisComplexType premis);
    T Read(File file);
    PremisComplexType Create(T premisFile); //, ExifMetadata? exifMetadata = null
    void Patch(PremisComplexType premis, T premisFile); //, ExifMetadata? exifMetadata = null
    string Serialise(PremisComplexType premis);
    XmlElement? GetXmlElement(PremisComplexType premis, bool fileElement);

    //TODO: below line will not be part of the interface
    //XmlElement? GetXmlElement(ExifTag exifMetdata, XmlDocument document);

    //FileFormatMetadata? Read(PremisComplexType premis);
    //FileFormatMetadata Read(File file);
    //PremisComplexType Create(FileFormatMetadata premisFile, ExifMetadata? exifMetadata = null);
    //void Patch(PremisComplexType premis, FileFormatMetadata premisFile, ExifMetadata? exifMetadata = null);
    //string Serialise(PremisComplexType premis);
    //XmlElement? GetXmlElement(PremisComplexType premis, bool fileElement);
    //XmlElement? GetXmlElement(ExifTag exifMetdata, XmlDocument document);
}
