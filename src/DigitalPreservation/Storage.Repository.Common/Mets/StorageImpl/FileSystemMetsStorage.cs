using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;

namespace Storage.Repository.Common.Mets.StorageImpl;

public class FileSystemMetsStorage(IMetsParser metsParser) : IMetsStorage
{
    public async Task<Result> WriteMets(FullMets fullMets)
    {
        if (fullMets.Uri.Scheme != "file")
        {
            return Result.Fail(ErrorCodes.BadRequest, fullMets.Uri.Scheme + " not supported");
        }
        var xml = StorageHelpers.XmlFromFullMets(fullMets);
        try
        {
            await File.WriteAllTextAsync(fullMets.Uri.LocalPath, xml);
            return Result.Ok();
        }
        catch (Exception e)
        {
            return Result.Fail(ErrorCodes.UnknownError, "Error writing METS to file: " + e.Message);
        }
    }


    public async Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch)
    {
        if (metsLocation.Scheme != "file")
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.BadRequest, metsLocation.Scheme + " not supported");
        }
        DigitalPreservation.XmlGen.Mets.Mets? mets;
        string? returnedETag;
        var fileLocResult = await metsParser.GetRootAndFile(metsLocation);
        var (_, file) = fileLocResult.Value;
        if (file is null)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.NotFound, "No METS file in " + metsLocation);
        }

        var fi = new FileInfo(file.LocalPath);
        try
        {
            returnedETag = Checksum.Sha256FromFile(fi);
            if (eTagToMatch is not null && returnedETag != eTagToMatch)
            {
                return Result.FailNotNull<FullMets>(
                    ErrorCodes.PreconditionFailed, "Supplied ETag did not match METS");
            }

            var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
            using var reader = XmlReader.Create(file.LocalPath);
            mets = (DigitalPreservation.XmlGen.Mets.Mets)serializer.Deserialize(reader)!;
        }
        catch (Exception e)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.UnknownError, e.Message);
        }
                
        if (StorageHelpers.GetAgentName(mets) != Constants.MetsCreatorAgent)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.BadRequest,
                "METS file was not created by " + Constants.MetsCreatorAgent);
        }
        
        var fullMetal = new FullMets
        {
            Mets = mets,
            Uri = file,
            ETag = returnedETag
        };
        return Result.OkNotNull(fullMetal);
    }
}