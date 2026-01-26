using System.Net;
using System.Xml;
using System.Xml.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;

namespace Storage.Repository.Common.Mets.StorageImpl;

public class S3MetsStorage(
    IAmazonS3 s3Client,
    IMetsParser metsParser) : IMetsStorage
{
    public async Task<Result> WriteMets(FullMets fullMets)
    {       
        if (fullMets.Uri.Scheme != "s3")
        {
            return Result.Fail(ErrorCodes.BadRequest, fullMets.Uri.Scheme + " not supported");
        }
        var xml = StorageHelpers.XmlFromFullMets(fullMets);
        if (string.IsNullOrEmpty(xml))
        {
            //This is unreachable due to the namespace injection GetNameSpaces() in StorageHelpers.cs
            return Result.Fail(ErrorCodes.BadRequest, "Failed to serialize METS");
        }

        var awsUri = new AmazonS3Uri(fullMets.Uri);
        var req = new PutObjectRequest
        {
            BucketName = awsUri.Bucket,
            Key = awsUri.Key,
            ContentType = "application/xml",
            ContentBody = xml,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256
        };
        if (fullMets.ETag is not null)
        {
            req.IfMatch = fullMets.ETag;
        }

        var resp = await s3Client.PutObjectAsync(req);

        return resp.HttpStatusCode switch
        {
            HttpStatusCode.Created or HttpStatusCode.OK => Result.Ok(),
            HttpStatusCode.PreconditionFailed => Result.Fail(ErrorCodes.PreconditionFailed,
                "Supplied ETag did not match METS"),
            _ => Result.Fail(ErrorCodes.BadRequest,
                "AWS returned HTTP Status " + resp.HttpStatusCode + " when writing METS")
        };
    }

        
    public async Task<Result<FullMets>> GetFullMets(Uri metsLocation, string? eTagToMatch)
    {
        if (metsLocation.Scheme != "s3")
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.BadRequest, metsLocation.Scheme + " not supported");
        }
        DigitalPreservation.XmlGen.Mets.Mets? mets = null;
        string? returnedETag;
        var fileLocResult = await metsParser.GetRootAndFile(metsLocation);
        var (_, file) = fileLocResult.Value;
        if (file is null)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.NotFound, "No METS file in " + metsLocation);
        }

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

        if (StorageHelpers.GetAgentName(mets) != Constants.MetsCreatorAgent)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.BadRequest,
                "METS file was not created by " + Constants.MetsCreatorAgent);
        }

        if (mets == null)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.UnknownError, "Unable to read METS");
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