using System.Net;
using System.Xml;
using System.Xml.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
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

        PutObjectResponse resp;
        try
        {
            resp = await s3Client.PutObjectAsync(req);
        }
        catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            // AmazonS3Client throws for every non-2xx response rather than returning it, so a
            // lost If-Match race never reaches the switch below - the PreconditionFailed arm
            // that used to sit there was dead code (issue #355). A lost race on a conditional
            // write isn't always reported as 412: AWS's own conditional-writes documentation
            // says a concurrent PUT can also come back as 409 Conflict, and a concurrent delete
            // of the same key can too - both are the same "someone else won the race" outcome
            // from this caller's point of view, so both map to PreconditionFailed (issue #355
            // review).
            return Result.Fail(ErrorCodes.PreconditionFailed, "Supplied ETag did not match METS");
        }
        catch (AmazonS3Exception e)
        {
            // Nothing else about an S3 failure here is a precondition race - but nothing about
            // writing METS should let an AmazonS3Exception escape uncaught either (issue #355
            // review): GetFullMets already guards every S3 failure this way; WriteMets did not.
            return Result.Fail(ErrorCodes.UnknownError, e.Message);
        }

        return resp.HttpStatusCode switch
        {
            HttpStatusCode.Created or HttpStatusCode.OK => Result.Ok(),
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
        var (root, file) = fileLocResult.Value;
        if (file is null)
        {
            return Result.FailNotNull<FullMets>(ErrorCodes.NotFound, "No METS file in " + metsLocation + " root location: " + root);
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

            returnedETag = resp.ETag;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // As in WriteMets: AmazonS3Client throws rather than returning a 412 response, so the
            // PreconditionFailed status check that used to sit in the try block above never saw a
            // lost ETag race (issue #355).
            return Result.FailNotNull<FullMets>(ErrorCodes.PreconditionFailed,
                "Supplied ETag did not match METS");
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
        MetsCache.Populate(fullMetal);
        return Result.OkNotNull(fullMetal);

    }

}