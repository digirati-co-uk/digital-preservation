using System.Xml.Linq;
using DigitalPreservation.Common.Model.Results;

namespace DigitalPreservation.Common.Model.Mets;

public interface IMetsParser
{
    /// <summary>
    /// Simple model that doesn't use the XmlSerializer
    /// </summary>
    /// <param name="metsLocation"></param>
    /// <param name="parse">Whether to actually load and parse the METS, or just obtain its file information</param>
    /// <returns></returns>
    Task<Result<MetsFileWrapper>> GetMetsFileWrapper(Uri metsLocation, bool parse = true);

    Result<MetsFileWrapper> GetMetsFileWrapperFromXDocument(Uri metsUri, XDocument metsXDocument);
    
    Task<Result<(Uri root, Uri? file)>> GetRootAndFile(Uri metsLocation);

}