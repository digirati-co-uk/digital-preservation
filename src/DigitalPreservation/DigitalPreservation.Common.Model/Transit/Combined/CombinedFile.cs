using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit.Combined;

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

            var bestContentType = ContentTypes.GetBestContentType(FileInDeposit);
            if (bestContentType != FileInMets.ContentType)
            {
                misMatches.Add(new FileMisMatch(nameof(FileFormatMetadata), "ContentType",
                    bestContentType, FileInMets.ContentType));
            }

            if (DepositFileFormatMetadata!.Digest != MetsFileFormatMetadata!.Digest)
            {
                misMatches.Add(new FileMisMatch(nameof(FileFormatMetadata), "Digest", DepositFileFormatMetadata.Digest,
                    MetsFileFormatMetadata.Digest));
            }
        }

        if (DepositVirusScanMetadata != null && MetsVirusScanMetadata != null)
        {
            if (DepositVirusScanMetadata.HasVirus != MetsVirusScanMetadata.HasVirus)
            {
                misMatches.Add(new FileMisMatch(nameof(VirusScanMetadata), "HasVirus",
                    DepositVirusScanMetadata.HasVirus.ToString(), MetsVirusScanMetadata.HasVirus.ToString()));
            }

            if (DepositVirusScanMetadata.VirusFound != MetsVirusScanMetadata.VirusFound)
            {
                misMatches.Add(new FileMisMatch(nameof(VirusScanMetadata), "VirusFound",
                    DepositVirusScanMetadata.VirusFound, MetsVirusScanMetadata.VirusFound));
            }

            if (DepositVirusScanMetadata.VirusDefinition?.Trim() != MetsVirusScanMetadata.VirusDefinition?.Trim())
            {
                misMatches.Add(new FileMisMatch(nameof(VirusScanMetadata), "VirusDefinition",
                    DepositVirusScanMetadata.VirusDefinition, MetsVirusScanMetadata.VirusDefinition));
            }
        }

        if (DepositVirusScanMetadata != null && MetsVirusScanMetadata == null)
        {
            misMatches.Add(new FileMisMatch(nameof(VirusScanMetadata), "(Missing section)",
                    "(virus check)", null));
        }

        if (DepositExifMetadata != null && MetsExifMetadata == null)
        {
            misMatches.Add(new FileMisMatch(nameof(ExifMetadata), "(Missing section)",
                "(exif metadata)", null));
        }

        if (DepositExifMetadata == null || MetsExifMetadata == null || DepositExifMetadata.Tags == null || MetsExifMetadata.Tags == null) return misMatches;

        var depositExifMetadata = DepositExifMetadata.Tags;
        var metsExifMetadata = MetsExifMetadata.Tags;

        var isEqual = depositExifMetadata.SequenceEqual(metsExifMetadata, new ExifTagComparer());

        if (isEqual) return misMatches;

        var inDepositNotInMets = depositExifMetadata.Except(metsExifMetadata, new ExifTagComparer());
        var arrayDeposit = inDepositNotInMets.ToArray();
        var inMetsNotInDeposit = metsExifMetadata.Except(depositExifMetadata, new ExifTagComparer());
        var arrayMets = inMetsNotInDeposit.ToArray();

        foreach (var exifItemDeposit in arrayDeposit.Select((value, i) => (value, i)))
        {
            var exifItemDepositTagName = exifItemDeposit.value.TagName;
            var exifItemDepositTagValue = exifItemDeposit.value.TagValue?.RemoveLineEndings();
            var depositItemArrayIndex = exifItemDeposit.i;

            if ((!isEqual && !arrayMets.Any()) || (arrayMets.Length < (depositItemArrayIndex + 1))) //will get an out of bound array exception
            {
                var itemInMetsArray = arrayMets.FirstOrDefault(x => x.TagName == exifItemDepositTagName && x.MismatchAdded == false);
                if (itemInMetsArray == null)
                {
                    //This handles duplicates with different values
                    var itemInMetsList = metsExifMetadata.FirstOrDefault(x => x.TagName == exifItemDepositTagName && x.MismatchAdded == false);
                    if (itemInMetsList != null && string.Equals(exifItemDepositTagValue, itemInMetsList.TagValue, StringComparison.CurrentCultureIgnoreCase))
                    {
                        itemInMetsList.MismatchAdded = true;
                        continue;
                    }

                    if (exifItemDepositTagName != null && itemInMetsList != null)
                    {
                        misMatches.Add(new FileMisMatch(nameof(ExifMetadata), exifItemDepositTagName, exifItemDepositTagValue, itemInMetsList.TagValue));
                        itemInMetsList.MismatchAdded = true;

                    }

                    continue;
                }


                if (string.Equals(exifItemDepositTagValue, itemInMetsArray.TagValue, StringComparison.CurrentCultureIgnoreCase))
                {
                    itemInMetsArray.MismatchAdded = true;
                    continue;
                }

                if (exifItemDepositTagName != null)
                {
                    misMatches.Add(new FileMisMatch(nameof(ExifMetadata), exifItemDepositTagName, exifItemDepositTagValue, itemInMetsArray.TagValue));
                    itemInMetsArray.MismatchAdded = true;
                    continue;
                }

            }

            var metsExifItem = arrayMets[depositItemArrayIndex];

            if (exifItemDepositTagName != null && metsExifItem.TagName != null && !string.Equals(exifItemDepositTagName, metsExifItem.TagName, StringComparison.CurrentCultureIgnoreCase)) continue;

            var metsTagValue = metsExifItem.TagValue?.RemoveLineEndings();
            if (string.Equals(exifItemDepositTagValue, metsTagValue, StringComparison.CurrentCultureIgnoreCase)) continue;

            if (exifItemDepositTagName == null) continue;
            arrayMets[depositItemArrayIndex].MismatchAdded = true;
            misMatches.Add(new FileMisMatch(nameof(ExifMetadata), exifItemDepositTagName, exifItemDepositTagValue, metsExifItem.TagValue));

        }

        var resultDeposit = depositExifMetadata.Where(p => metsExifMetadata.All(p2 => p2.TagName != p.TagName));
        misMatches.AddRange(resultDeposit.Select(depositField => new FileMisMatch(nameof(ExifMetadata), depositField.TagName ?? string.Empty, depositField.TagValue, "field does not exist in METS")));

        var resultMets = metsExifMetadata.Where(p => depositExifMetadata.All(p2 => p2.TagName != p.TagName));
        misMatches.AddRange(resultMets.Select(metsField => new FileMisMatch(nameof(ExifMetadata), metsField.TagName ?? string.Empty, metsField.TagValue, "field does not exist in deposit")));

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

    public ExifMetadata? GetExifMetadata()
    {
        ExifMetadata? exifMetadata = null;
        if (FileInDeposit != null)
        {
            exifMetadata = FileInDeposit.GetExifMetadata();
        }

        if (exifMetadata is null && FileInMets != null)
        {
            exifMetadata = FileInMets.GetExifMetadata();
        }
        return exifMetadata;
    }

    private FileFormatMetadata? cachedDepositFileFormatMetadata;
    private bool haveScannedDepositFileFormatMetadata;
    private VirusScanMetadata? cachedDepositVirusScanMetadata;
    private bool haveScannedDepositVirusScanMetadata;
    private ExifMetadata? cachedDepositExifMetadata;
    private bool haveScannedDepositExifMetadata;
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
            cachedDepositFileFormatMetadata = FileInDeposit?.GetFileFormatMetadata();
            haveScannedDepositFileFormatMetadata = true;
            return cachedDepositFileFormatMetadata;
        }
    }

    public VirusScanMetadata? DepositVirusScanMetadata
    {
        get
        {
            if (haveScannedDepositVirusScanMetadata)
            {
                return cachedDepositVirusScanMetadata;
            }
            cachedDepositVirusScanMetadata = FileInDeposit?.GetVirusScanMetadata();
            haveScannedDepositVirusScanMetadata = true;
            return cachedDepositVirusScanMetadata;
        }
    }

    public ExifMetadata? DepositExifMetadata
    {
        get
        {
            if (haveScannedDepositExifMetadata)
            {
                return cachedDepositExifMetadata;
            }
            cachedDepositExifMetadata = FileInDeposit?.GetExifMetadata();
            haveScannedDepositExifMetadata = true;
            return cachedDepositExifMetadata;
        }
    }


    private FileFormatMetadata? cachedMetsFileFormatMetadata;
    private bool haveScannedMetsFileFormatMetadata;
    private VirusScanMetadata? cachedMetsVirusScanMetadata;
    private bool haveScannedMetsVirusScanMetadata;
    private ExifMetadata? cachedMetsExifMetadata;
    private bool haveScannedMetsExifMetadata;

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

            cachedMetsFileFormatMetadata = FileInMets?.GetFileFormatMetadata();
            haveScannedMetsFileFormatMetadata = true;
            return cachedMetsFileFormatMetadata;
        }
    }

    /// <summary>
    /// We use the METS virus scan metadata multiple times, so let's cache it
    /// </summary>
    /// <returns></returns>
    public VirusScanMetadata? MetsVirusScanMetadata
    {
        get
        {
            if (haveScannedMetsVirusScanMetadata)
            {
                return cachedMetsVirusScanMetadata;
            }

            cachedMetsVirusScanMetadata = FileInMets?.GetVirusScanMetadata();
            haveScannedMetsVirusScanMetadata = true;
            return cachedMetsVirusScanMetadata;
        }
    }

    /// <summary>
    /// We use the METS virus scan metadata multiple times, so let's cache it
    /// </summary>
    /// <returns></returns>
    public ExifMetadata? MetsExifMetadata
    {
        get
        {
            if (haveScannedMetsExifMetadata)
            {
                return cachedMetsExifMetadata;
            }

            cachedMetsExifMetadata = FileInMets?.GetExifMetadata();
            haveScannedMetsExifMetadata = true;
            return cachedMetsExifMetadata;
        }
    }

    public long GetSingleSize()
    {
        long size;
        var distinctSizes = new List<long?>
        {
            FileInMets?.Size ?? 0,
            DepositFileFormatMetadata?.Size,
            FileInDeposit?.Size
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
    
    
    public string? GetSingleContentTypeFromMetsAndDeposit()
    {
        List<string?> allContentTypes =
        [
            FileInMets?.ContentType,
            ContentTypes.GetBestContentType(FileInDeposit)
        ];
        var distinctContentTypes = allContentTypes
            .Where(ct => ct.HasText()).Distinct().Select(ct => ct!).ToList();
        ContentTypes.RemoveExtraGenericContentTypes(distinctContentTypes);
        if (distinctContentTypes.Count == 1)
        {
            return distinctContentTypes.Single();
        }
        return null;
    }

    public List<string> GetDistinctDigests()
    {
        var distinctDigests = new List<string?>
        {
            FileInMets?.Digest,
            DepositFileFormatMetadata?.Digest,
            FileInDeposit?.Digest
        }.Where(digest => digest.HasText())
            .Distinct()
            .Select(digest => digest!) // flip to non-nullable
            .ToList();
        return distinctDigests;
    }

    public string? GetSingleDigest()
    {
        var distinctDigests = GetDistinctDigests();
        return distinctDigests.Count == 1 ? distinctDigests.Single() : null;
    }

    public string? GetName()
    {
        return FileInMets?.Name ?? FileInDeposit?.Name ?? FileInMets?.GetSlug() ?? FileInDeposit?.GetSlug();
    }
}

public class ExifTagComparer : IEqualityComparer<ExifTag>
{
    public bool Equals(ExifTag? x, ExifTag? y)
    {
        //Check whether the compared objects reference the same data. 
        if (ReferenceEquals(x, y))
            return true;

        //Check whether any of the compared objects is null. 
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
            return false;

        return string.Equals(x.TagName, y.TagName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.TagValue, y.TagValue, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(ExifTag exifTag)
    {
        //Check whether the object is null 
        if (ReferenceEquals(exifTag, null))
            return 0;

        //Get hash code for the name field if it is not null
        var tagNameHashCode = !string.IsNullOrEmpty(exifTag.TagName) ? 0 : exifTag.TagName?.GetHashCode() ?? 0;
        var tagValueHashCode = !string.IsNullOrEmpty(exifTag.TagValue) ? 0 : exifTag.TagValue?.GetHashCode() ?? 0;
        // Get hash code for marks also if it is not 0

        return tagNameHashCode ^ tagValueHashCode;
    }
}