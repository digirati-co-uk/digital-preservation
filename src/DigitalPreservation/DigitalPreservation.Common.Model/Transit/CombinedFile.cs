using System.ComponentModel;
using System.Reflection;
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

    private List<FileMisMatch>? fileMisMatches;

    /// <summary>
    /// Discrepancies between the metadata in the METS file and metadata derived from the contents of the deposit,
    /// in particular tool outputs.
    /// </summary>
    public List<FileMisMatch> MisMatches => fileMisMatches ??= GenerateMisMatches();

    private List<FileMisMatch> GenerateMisMatches()
    {
        List<FileMisMatch> misMatches = [];
        if (FileInDeposit == null || FileInMets == null)
        {
            // mismatches are only when both are present
            return misMatches; 
        }

        if (FolderNames.IsMetadata(LocalPath!))
        {
            return misMatches;
        }

        if (LocalPath == "mets.xml")
        {
            return misMatches;
        }
        
        // (temp) do this just for FileFormatMetadata initially
        if (DepositFileFormatMetadata != null && MetsFileFormatMetadata != null)
        {
            // not a mismatch if one or other doesn't have any metadata yet
            if (DepositFileFormatMetadata.FormatName != MetsFileFormatMetadata.FormatName)
            {
                misMatches.Add(new FileMisMatch(nameof(FileFormatMetadata), "FormatName",
                    DepositFileFormatMetadata.FormatName, MetsFileFormatMetadata.FormatName));
            }

            if (DepositFileFormatMetadata!.PronomKey != MetsFileFormatMetadata!.PronomKey)
            {
                misMatches.Add(new FileMisMatch(nameof(FileFormatMetadata), "PronomKey",
                    DepositFileFormatMetadata.PronomKey, MetsFileFormatMetadata.PronomKey));
            }

            if (DepositFileFormatMetadata!.ContentType != FileInMets.ContentType)
            {
                misMatches.Add(new FileMisMatch(nameof(FileFormatMetadata), "ContentType",
                    DepositFileFormatMetadata.ContentType, FileInMets.ContentType));
            }

            if (DepositFileFormatMetadata!.Digest != MetsFileFormatMetadata!.Digest)
            {
                misMatches.Add(new FileMisMatch(nameof(FileFormatMetadata), "Digest", DepositFileFormatMetadata.Digest,
                    MetsFileFormatMetadata.Digest));
            }
        }

        return misMatches;
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

    public class FileMisMatch(string type, string field, string? valueInDeposit, string? valueInMets)
    {
        public string MetadataType { get; } = type;
        public string Field { get; } = field;
        public string? ValueInDeposit { get; } = valueInDeposit;
        public string? ValueInMets { get; } = valueInMets;

        public override string ToString()
        {
            return $"{Field}: {ValueInDeposit} => {ValueInMets} ({MetadataType})";
        }
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
    private bool haveScannedDepositFileFormatMetadata;
    /// <summary>
    /// We use the deposit file format metadata multiple times, so let's cache it
    /// </summary>
    /// <returns></returns>
    public FileFormatMetadata? DepositFileFormatMetadata
    {
        get
        {
            if (haveScannedDepositFileFormatMetadata)
            {
                return cachedDepositFileFormatMetadata;
            }
            cachedDepositFileFormatMetadata = fileInDeposit?.GetFileFormatMetadata();
            haveScannedDepositFileFormatMetadata = true;
            return cachedDepositFileFormatMetadata;
        }
    }


    private FileFormatMetadata? cachedMetsFileFormatMetadata;
    private bool haveScannedMetsFileFormatMetadata;

    /// <summary>
    /// We use the deposit file format metadata multiple times, so let's cache it
    /// </summary>
    /// <returns></returns>
    public FileFormatMetadata? MetsFileFormatMetadata
    {
        get
        {
            if (haveScannedMetsFileFormatMetadata)
            {
                return cachedMetsFileFormatMetadata;
            }

            cachedMetsFileFormatMetadata = fileInMets?.GetFileFormatMetadata();
            haveScannedMetsFileFormatMetadata = true;
            return cachedMetsFileFormatMetadata;
        }
    }

    public long GetSingleSize()
    {
        long size;
        var distinctSizes = new List<long?>
        {
            fileInMets?.Size ?? 0,
            DepositFileFormatMetadata?.Size,
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
            DepositFileFormatMetadata?.ContentType,
            fileInDeposit?.ContentType
        ];
    }
    
    public string? GetSingleContentType()
    {
        var distinctContentTypes = GetAllContentTypes().Where(ct => ct.HasText()).Distinct().ToList();
        if (distinctContentTypes.Count > 1)
        {
            // It might really be application/octet-stream, which is OK if that's the best we can do
            distinctContentTypes.RemoveAll(ct => ct == "application/octet-stream");
        }
        if (distinctContentTypes.Count > 1)
        {
            // It might really be application/octet-stream, which is OK if that's the best we can do
            distinctContentTypes.RemoveAll(ct => ct == "binary/octet-stream");
        }
        if (distinctContentTypes.Count == 1)
        {
            return distinctContentTypes.Single();
        }
        return null;
    }

    public string? GetSingleDigest()
    {
        var distinctDigests = new List<string?>
        {
            fileInMets?.Digest,
            DepositFileFormatMetadata?.Digest,
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