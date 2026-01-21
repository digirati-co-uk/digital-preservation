using System.Xml.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Utils;
using Checksum = DigitalPreservation.Utils.Checksum;

namespace Storage.Repository.Common.Mets.StorageImpl;

public class S3MetsLoader(IAmazonS3 s3Client) : IMetsLoader
{
    public async Task<Uri?> FindMetsFile(Uri root)
    {    
        if (root.Scheme != "s3")
        {
            throw new NotSupportedException(root.Scheme + " not supported");
        }    
        Uri? file = null;
        var rootS3Uri = new AmazonS3Uri(root);
        var prefix = $"{rootS3Uri.Key.TrimEnd('/')}/";
            
        // Need to find the METS
        var listObjectsReq = new ListObjectsV2Request
        {
            BucketName = rootS3Uri.Bucket,
            Prefix = prefix,
            Delimiter = "/" // first "children" only ... does that return "data/" no?                       
        };
        var resp = await s3Client.ListObjectsV2Async(listObjectsReq);
        var files = resp.S3Objects.Where(s => !s.Key.EndsWith('/')).ToList();
        var firstXmlKey = files.FirstOrDefault(s => MetsUtils.IsMetsFile(s.Key.GetSlug(), true));
        if (firstXmlKey == null)
        {
            firstXmlKey = files.FirstOrDefault(s => MetsUtils.IsMetsFile(s.Key.GetSlug(), false));
        }
                
        if (firstXmlKey == null)
        {
            listObjectsReq = new ListObjectsV2Request
            {
                BucketName = rootS3Uri.Bucket,
                Prefix = prefix + "data/", // BagIt layout
                Delimiter = "/"                       
            };
            resp = await s3Client.ListObjectsV2Async(listObjectsReq);
            files = resp.S3Objects.Where(s => !s.Key.EndsWith('/')).ToList();
            firstXmlKey = files.FirstOrDefault(s => MetsUtils.IsMetsFile(s.Key.GetSlug(), true));
            if (firstXmlKey == null)
            {
                firstXmlKey = files.FirstOrDefault(s => MetsUtils.IsMetsFile(s.Key.GetSlug(), false));
            }
        }

        if (firstXmlKey != null)
        {
            file = new Uri($"s3://{firstXmlKey.BucketName}/{firstXmlKey.Key}");
        }

        return file;
    }


    public async Task<WorkingFile?> LoadMetsFileAsWorkingFile(Uri file)
    {
        // This "find the METS file" logic is VERY basic and doesn't even look at the file.
        // But this is just for Proof of Concept.
        var fileS3Uri = new AmazonS3Uri(file);
        try
        {
            var resp = await s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = fileS3Uri.Bucket,
                Key = fileS3Uri.Key
            });
        }

        catch (AmazonS3Exception ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            throw;
        }
        
        var s3Stream = await s3Client.GetObjectStreamAsync(fileS3Uri.Bucket, fileS3Uri.Key, null);
        var digest = Checksum.Sha256FromStream(s3Stream)?.ToLowerInvariant();
        var name = fileS3Uri.Key.GetSlug(); // because mets is in root - whether apparent (BagIt) or real
        return new WorkingFile
        {
            ContentType = "application/xml",
            LocalPath = name, 
            Name = name,
            Digest = digest
        };
    }
    
    
    public async Task<(XDocument?, string)> ExamineXml(Uri file, string? digest, bool parse)
    {
        XDocument? xDoc = null;
        var s3Uri = new AmazonS3Uri(file);
        var resp = await s3Client.GetObjectAsync(s3Uri.Bucket, s3Uri.Key);
        var s3ETag = resp.ETag!;
        if (parse)
        {
            xDoc = await XDocument.LoadAsync(resp.ResponseStream, LoadOptions.None, CancellationToken.None);
        }
        return (xDoc, s3ETag);
    }
}