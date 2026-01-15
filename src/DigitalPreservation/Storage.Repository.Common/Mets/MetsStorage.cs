using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using Checksum = DigitalPreservation.Utils.Checksum;

namespace Storage.Repository.Common.Mets;

public class MetsStorage(
    IAmazonS3 s3Client,
    IMetsParser metsParser
) : IMetsStorage
{
    private static XmlSerializerNamespaces GetNamespaces()
    {
        var ns = new XmlSerializerNamespaces();
        ns.Add("mets", "http://www.loc.gov/METS/");
        ns.Add("mods", "http://www.loc.gov/mods/v3");
        ns.Add("premis", "http://www.loc.gov/premis/v3");
        ns.Add("xlink", "http://www.w3.org/1999/xlink");
        ns.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
        return ns;
    }

    //Maybe public and have some test coverage?
    private static string? XmlFromFullMets(FullMets fullMets)
    {
        var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
               {
                   OmitXmlDeclaration = true,
                   NamespaceHandling = NamespaceHandling.OmitDuplicates,
               }))
        {
            serializer.Serialize(writer, fullMets.Mets, GetNamespaces());
        }

        return sb.ToString();
    }

    private async Task<Result> WriteMetsToFile(Uri fileUri, string? xml)
    {
        try
        {
            await File.WriteAllTextAsync(fileUri.LocalPath, xml);
            return Result.Ok();
        }
        catch (Exception e)
        {
            return Result.Fail(ErrorCodes.UnknownError, "Error writing METS to file: " + e.Message);
        }
    }

    private async Task<Result> WriteMetsToS3(Uri s3Uri, string? xml, string? eTag)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return Result.Fail(ErrorCodes.BadRequest, "Failed to serialize METS");
        }

        var awsUri = new AmazonS3Uri(s3Uri);
        var req = new PutObjectRequest
        {
            BucketName = awsUri.Bucket,
            Key = awsUri.Key,
            ContentType = "application/xml",
            ContentBody = xml,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256
        };
        if (eTag is not null)
        {
            req.IfMatch = eTag;
        }

        var resp = await s3Client.PutObjectAsync(req);

        if (resp.HttpStatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
        {
            return Result.Ok();
        }

        if (resp.HttpStatusCode is HttpStatusCode.PreconditionFailed)
        {
            return Result.Fail(ErrorCodes.PreconditionFailed, "Supplied ETag did not match METS");
        }

        return Result.Fail(ErrorCodes.BadRequest,
            "AWS returned HTTP Status " + resp.HttpStatusCode + " when writing METS");


    }

    public async Task<Result> WriteMets(FullMets fullMets)
    {
        var xml = XmlFromFullMets(fullMets);

        var result = fullMets.Uri.Scheme switch  //Enum better?
        {
            "file" => await WriteMetsToFile(fullMets.Uri, xml),
            "s3" => await WriteMetsToS3(fullMets.Uri, xml, fullMets.ETag),
            _ => Result.Fail(ErrorCodes.BadRequest, fullMets.Uri.Scheme + " not supported")
        };

        return result;
    }


    private async Task<Result<FullMets>> GetFullMetsFile(Uri metsLocation, string? eTagToMatch)
    {
        Result<FullMets> result;

        DigitalPreservation.XmlGen.Mets.Mets? mets = null;
        var fileLocResult = await metsParser.GetRootAndFile(metsLocation);
        var (_, file) = fileLocResult.Value;

        var fi = new FileInfo(file.LocalPath);
        try
        {
            var returnedETag = Checksum.Sha256FromFile(fi);
            if (eTagToMatch is not null && returnedETag != eTagToMatch)
            {
                result = Result.FailNotNull<FullMets>(
                    ErrorCodes.PreconditionFailed, "Supplied ETag did not match METS");
            }

            var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
            using var reader = XmlReader.Create(file.LocalPath);
            mets = (DigitalPreservation.XmlGen.Mets.Mets)serializer.Deserialize(reader)!;

            result = Result.OkNotNull<FullMets>(new FullMets
            {
                Mets = mets,
                Uri = file,
                ETag = returnedETag
            });

        }
        catch (Exception e)
        {
            result = Result.FailNotNull<FullMets>(ErrorCodes.UnknownError, e.Message);
        }

        return result;    

    }

    public async Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch)
    {
        DigitalPreservation.XmlGen.Mets.Mets? mets = null;
        var fileLocResult = await metsParser.GetRootAndFile(metsLocation);
        var (_, file) = fileLocResult.Value;
        if (file is null)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.NotFound, "No METS file in " + metsLocation);
        }

        string? returnedETag;

        switch (file.Scheme)
        {
            case "s3":
                var s3Uri = new AmazonS3Uri(file);
                var gor = new GetObjectRequest
                {
                    BucketName = s3Uri.Bucket,
                    Key = s3Uri.Key,
                };
                if (eTagToMatch is not null)
                {
                    gor.EtagToMatch = eTagToMatch;
                }

                try
                {
                    var resp = await s3Client.GetObjectAsync(gor);
                    if (resp.HttpStatusCode == HttpStatusCode.OK)
                    {
                        var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
                        using var reader = XmlReader.Create(resp.ResponseStream);
                        mets = (DigitalPreservation.XmlGen.Mets.Mets)serializer.Deserialize(reader)!;
                    }

                    if (resp.HttpStatusCode is HttpStatusCode.PreconditionFailed)
                    {
                        return Result.FailNotNull<FullMets>(ErrorCodes.PreconditionFailed,
                            "Supplied ETag did not match METS");
                    }

                    returnedETag = resp.ETag;
                }
                catch (Exception e)
                {
                    return Result.FailNotNull<FullMets>(ErrorCodes.UnknownError, e.Message);
                }

                break;

            case "file":
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

                break;

            default:
                return Result.FailNotNull<FullMets>(ErrorCodes.BadRequest, file.Scheme + " not supported");
        }


        string? agentName = null;
        if (mets?.MetsHdr?.Agent is not null && mets.MetsHdr.Agent.Count > 0)
        {
            agentName = mets.MetsHdr.Agent[0].Name;
        }

        if (agentName != Constants.MetsCreatorAgent)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.BadRequest,
                "METS file was not created by " + Constants.MetsCreatorAgent);
        }

        if (mets != null)
        {
            var fullMetal = new FullMets
            {
                Mets = mets,
                Uri = file,
                ETag = returnedETag
            };
            return Result.OkNotNull(fullMetal);
        }

        return Result.FailNotNull<FullMets>(
            ErrorCodes.UnknownError, "Unable to read METS");
    }
}