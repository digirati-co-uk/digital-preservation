using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public class CombinedFile(WorkingFile? fileInDeposit, WorkingFile? fileInMets, string? relativePath = null)
{
    public string? LocalPath
    {
        get
        {
            if (relativePath == null)
            {
                return FileInDeposit?.LocalPath ?? FileInMets?.LocalPath;
            }
            if (FileInDeposit == null)
            {
                return FileInMets?.LocalPath;
            }

            if (FileInDeposit.LocalPath.StartsWith($"{relativePath}/"))
            {
                return FileInDeposit.LocalPath.RemoveStart($"{relativePath}/");
            }
            // We're in the root of a BagIt 
            return "../" +  FileInDeposit.LocalPath;
        }
    }
    
    public WorkingFile? FileInDeposit { get; private set; } = fileInDeposit;
    public WorkingFile? FileInMets { get; private set; } = fileInMets;

    public void DeleteFileInDeposit()
    {
        FileInDeposit = null;
    }
    
    public void DeleteFileInMets()
    {
        FileInMets = null;
    }
    
    public FileMisMatch<string>? GetNameMisMatch()
    {
        if (FileInDeposit is null || FileInMets is null)
        {
            return null;
        }
        
        return new FileMisMatch<string>(FileInDeposit.Name, FileInMets.Name);
    }
    
    public FileMisMatch<string>? GetDigestMisMatch()
    {
        if (FileInDeposit is null || FileInMets is null)
        {
            return null;
        }
        
        return new FileMisMatch<string>(FileInDeposit.Digest, FileInMets.Digest);
    }
    
    public FileMisMatch<long?>? GetSizeMisMatch()
    {
        if (FileInDeposit is null || FileInMets is null)
        {
            return null;
        }
        
        return new FileMisMatch<long?>(FileInDeposit.Size, FileInMets.Size);
    }
    
    public FileMisMatch<string>? GetContentTypeMisMatch()
    {
        if (FileInDeposit is null || FileInMets is null)
        {
            return null;
        }
        
        return new FileMisMatch<string>(FileInDeposit.ContentType, FileInMets.ContentType);
    }
    
    public Whereabouts Whereabouts
    {
        get
        {
            if (FileInDeposit is not null && FileInMets is not null)
            {
                return Whereabouts.Both;
            }

            if (FileInDeposit is not null)
            {
                if (relativePath.HasText() && !FileInDeposit.LocalPath.StartsWith(relativePath))
                {
                    return Whereabouts.Extra;
                }
                return Whereabouts.Deposit;
            }

            if (FileInMets is not null)
            {
                return Whereabouts.Mets;
            }

            return Whereabouts.Neither;
        }
    }

    public class FileMisMatch<T>(T? valueInDeposit, T? valueInMets)
    {
        public T? ValueInDeposit = valueInDeposit;
        public T? ValueInMets = valueInMets;
    }

    public VirusScanMetadata? GetVirusMetadata()
    {
        VirusScanMetadata? virusScanMetadata = null;
        if (FileInDeposit != null)
        {
            virusScanMetadata = FileInDeposit.GetVirusScanMetadata();
        }

        if (virusScanMetadata is null && FileInMets != null)
        {
            virusScanMetadata = FileInMets.GetVirusScanMetadata();
        }
        return virusScanMetadata;
    }

    private FileFormatMetadata? cachedDepositFileFormatMetadata;

    public FileFormatMetadata? GetCachedDepositFileFormatMetadata()
    {
        cachedDepositFileFormatMetadata ??= fileInDeposit?.GetFileFormatMetadata();
        return cachedDepositFileFormatMetadata;
    }
    
    public long GetSingleSize()
    {
        cachedDepositFileFormatMetadata ??= fileInDeposit?.GetFileFormatMetadata();
        long size;
        var distinctSizes = new List<long?>
        {
            fileInMets?.Size ?? 0,
            cachedDepositFileFormatMetadata?.Size,
            fileInDeposit?.Size
        }.Where(s => s is > 0).Distinct().ToList();
        if (distinctSizes.Count != 1)
        {
            size = -1;
        }
        else
        { 
            size = distinctSizes.Single()!.Value; // why need a ! here? Pattern match means cannot be null...
        }

        return size;
    }

    public List<string?> GetAllContentTypes()
    {
        return
        [
            fileInMets?.ContentType,
            cachedDepositFileFormatMetadata?.ContentType,
            fileInDeposit?.ContentType
        ];
    }
    
    public string? GetSingleContentType()
    {
        cachedDepositFileFormatMetadata ??= fileInDeposit?.GetFileFormatMetadata();
        var distinctContentTypes = GetAllContentTypes().Where(ct => ct.HasText()).Distinct().ToList();
        if (distinctContentTypes.Count > 1)
        {
            // It might really be application/octet-stream, which is OK if that's the best we can do
            distinctContentTypes.RemoveAll(ct => ct == "application/octet-stream");
        }
        if (distinctContentTypes.Count == 1)
        {
            return distinctContentTypes.Single();
        }
        return null;
    }

    public string? GetSingleDigest()
    {
        cachedDepositFileFormatMetadata ??= fileInDeposit?.GetFileFormatMetadata();
        var distinctDigests = new List<string?>
        {
            fileInMets?.Digest,
            cachedDepositFileFormatMetadata?.Digest,
            fileInDeposit?.Digest
        }.Where(digest => digest.HasText()).Distinct().ToList();
        if (distinctDigests.Count == 1)
        {
            return distinctDigests.Single();
        }
        return null;
    }

    public string? GetName()
    {
        return fileInMets?.Name ?? fileInDeposit?.Name ?? fileInMets?.GetSlug() ?? fileInDeposit?.GetSlug();
    }
}