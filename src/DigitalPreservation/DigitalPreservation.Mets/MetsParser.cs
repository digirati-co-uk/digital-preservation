using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using Microsoft.Extensions.Logging;

namespace DigitalPreservation.Mets;

public class MetsParser(
    IMetsLoader loader,
    ILogger<MetsParser> logger) : IMetsParser
{
    /// <summary>
    /// Pre-built lookup dictionaries for O(1) access to METS elements by ID.
    /// This mirrors the Python implementation's amd_map, file_map, and tech_map.
    /// </summary>
    private sealed record MetsLookupMaps(
        Dictionary<string, XElement> AmdSecMap,
        Dictionary<string, XElement> DmdSecMap,
        Dictionary<string, XElement> FileMap,
        Dictionary<string, XElement> TechMdMap,
        Dictionary<string, XElement> DigiprovMdMap
    );
    
    /// <summary>
    /// Builds lookup dictionaries for efficient O(1) access to METS elements by ID.
    /// This is equivalent to the Python version's amd_map, file_map, and tech_map.
    /// </summary>
    private static MetsLookupMaps BuildLookupMaps(XDocument xMets)
    {
        // Build amdSec map: ID -> XElement
        var amdSecMap = xMets.Descendants(XNames.MetsAmdSec)
            .Where(el => el.Attribute("ID") != null)
            .ToDictionary(el => el.Attribute("ID")!.Value, el => el);

        // Build DMD Sec map: ID -> XElement (for access conditions, rights, recordInfo)
        var dmdSecMap = xMets.Descendants(XNames.MetsDmdSec)
            .Where(el => el.Attribute("ID") != null)
            .ToDictionary(el => el.Attribute("ID")!.Value, el => el);
        
        // Build file map: ID -> XElement (from fileSec)
        var fileMap = xMets.Descendants(XNames.MetsFile)
            .Where(el => el.Attribute("ID") != null)
            .ToDictionary(el => el.Attribute("ID")!.Value, el => el);

        // Build techMD map: ID -> XElement
        var techMdMap = xMets.Descendants(XNames.MetsTechMD)
            .Where(el => el.Attribute("ID") != null)
            .ToDictionary(el => el.Attribute("ID")!.Value, el => el);

        // Build digiprovMD map: ID -> XElement (for virus scan lookups)
        var digiprovMdMap = xMets.Descendants(XNames.MetsDigiprovMD)
            .Where(el => el.Attribute("ID") != null)
            .ToDictionary(el => el.Attribute("ID")!.Value, el => el);

        return new MetsLookupMaps(amdSecMap, dmdSecMap, fileMap, techMdMap, digiprovMdMap);
    }
    
    public async Task<Result<(Uri root, Uri? file)>> GetRootAndFile(Uri metsLocation)
    {
        // If metsLocation ends with .xml, it's assumed to be the METS file itself.
        // If not, it's assumed to be either its DIRECT containing directory / key,
        // or if the location contains no direct XML files but does contain a data/ directory
        // then we look in the data directory (a BagIt layout).
        // No other possibilities are supported.
        Uri root;
        Uri? file = null;
        var slug = metsLocation.GetSlug();
        if (slug.HasText() && slug.ToLowerInvariant().EndsWith(".xml"))
        {
            // If we have been explicitly given an .xml path, we assume the caller knows this is METS
            file = metsLocation;
            root = metsLocation.GetParentUri(trimTrailingSlash:false)!;
        }
        else
        {
            if (metsLocation.AbsoluteUri.EndsWith("/"))
            {
                root = metsLocation;
            }
            else
            {
                root = new Uri(metsLocation.AbsoluteUri + "/");
            }
        }

        if (file is not null)
        {
            // We assume that the caller knew that metsLocation is a file
            return Result.Ok((root, (Uri?)file));
        }

        // we haven't found the METS file yet
        file = await loader.FindMetsFile(root);
        return Result.Ok((root, file));
    }


    public async Task<Result<MetsFileWrapper>> GetMetsFileWrapper(Uri metsLocation, bool parse = true)
    {
        // might be a file path or an S3 URI
        var fileLocResult = await GetRootAndFile(metsLocation);
        var (root, file) = fileLocResult.Value;
        var mets = new MetsFileWrapper
        {
            RootUri = root,
            MetsUri = file,
            PhysicalStructure = WorkingDirectory.RootDirectory()
        };

        if (file is not null)
        {
            try
            {
                mets.Self = await loader.LoadMetsFileAsWorkingFile(file);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Unable to Load Mets File");
                return Result.FailNotNull<MetsFileWrapper>(ErrorCodes.UnknownError, e.Message);
            }
        }
        if(file is not null && mets.Self is not null)
        {
            try
            {
                var (xMets, eTag) = await loader.ExamineXml(file, mets.Self.Digest, parse);
                mets.ETag = eTag;
                if (parse && xMets is not null)
                {
                    PopulateFromMets(mets, xMets);
                }
                mets.XDocument = xMets;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Unable to Parse Mets XML File");
                return Result.FailNotNull<MetsFileWrapper>(ErrorCodes.UnknownError, e.Message);
            }
            if (mets.PhysicalStructure.FindFile(mets.Self.LocalPath) is null)
            {
                // If the METS file doesn't include itself in the root
                mets.PhysicalStructure.Files.Add(mets.Self);
            }
            if (mets.Files.SingleOrDefault(f => f.LocalPath == mets.Self.LocalPath) is null)
            {
                // and in the flat list
                mets.Files.Add(mets.Self);
            }
            mets.Editable = mets.Agent == Constants.MetsCreatorAgent;
        }
        return Result.OkNotNull(mets);
    }


    public Result<MetsFileWrapper> GetMetsFileWrapperFromXDocument(Uri metsUri, XDocument metsXDocument)
    {
        var mets = new MetsFileWrapper
        {
            MetsUri = metsUri,
            RootUri = metsUri.GetParentUri(),
            XDocument = metsXDocument,
            PhysicalStructure = WorkingDirectory.RootDirectory()
        };
        PopulateFromMets(mets, metsXDocument);
        mets.Editable = mets.Agent == Constants.MetsCreatorAgent;
        return Result.OkNotNull(mets);
    }


    private void PopulateFromMets(MetsFileWrapper mets, XDocument xMets)
    {
        var modsScope = xMets.Descendants(XNames.mods + "mods").FirstOrDefault();
        // EPrints mods is not wrapped in a <mods:mods> element
        if (modsScope == null)
        {
            modsScope = xMets.Root;
        }

        var modsTitle = modsScope?.Descendants(XNames.mods + "title").FirstOrDefault()?.Value;
        var modsName = modsScope?.Descendants(XNames.mods + "name").FirstOrDefault()?.Value;
        string? name = modsTitle ?? modsName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            mets.Name = name;
        }

        var agent = xMets.Descendants(XNames.mets + "agent").FirstOrDefault();
        if (agent is not null)
        {
            mets.Agent = agent.Descendants(XNames.mets + "name").FirstOrDefault()?.Value;
        }

        // There may be more than one, and they may or may not be qualified as physical or logical
        XElement? physicalStructMap = null;
        foreach (var sm in xMets.Descendants(XNames.MetsStructMap))
        {
            var typeAttr = sm.Attribute("TYPE");
            if (typeAttr?.Value != null)
            {
                if (typeAttr.Value.ToLowerInvariant() == "physical")
                {
                    physicalStructMap = sm;
                    break;
                }

                if (typeAttr.Value.ToLowerInvariant() == "logical")
                {
                    continue;
                }
            }

            if (physicalStructMap == null)
            {
                // This may get overwritten if we find a better one in the loop
                // EPRints METS files structMap don't have type
                physicalStructMap = sm;
            }
        }

        if (physicalStructMap == null)
        {
            throw new NotSupportedException("METS file must have a physical structMap");
        }

        // Now walk down the structMap
        // Each div either contains 1 (or sometimes more) mets:fptr, or it contains child DIVs.
        // If a DIV containing a mets:fptr has a LABEL (not ORDERLABEL) then that is the name of the file
        // If those DIVs have TYPE="Directory" and a LABEL, that gives us the name of the directory.
        // We need to see the path of the file, too.

        // A DIV TYPE="Directory" should never directly contain a file

        // GOOBI METS at Wellcome contain images and ALTO in the same DIV; the ADM_ID is for the Image not the ALTO.
        // Not sure how to be formal about that.

        var parent = physicalStructMap;

        // This relies on all directories having labels not just some
        Stack<string> directoryLabels = new();
        
        // Build lookup maps once before traversal for O(1) access during processing
        var lookupMaps = BuildLookupMaps(xMets);

        var filesWithExplicitRights = new HashSet<string>();
        ProcessChildStructDivs(mets, parent, directoryLabels, lookupMaps, filesWithExplicitRights);

        // We should now have a flat list of WorkingFile, and a set of WorkingDirectories, with correct names
        // if supplied. Now assign the files to their directories.

        foreach (var file in mets.Files)
        {
            var folder = mets.PhysicalStructure!.FindDirectory(file.LocalPath.GetParent(), false);
            if (folder is null)
            {
                throw new Exception("Our folder logic is wrong");
            }

            folder.Files.Add(file);
        }

        // Parse structLink file-to-file links
        ParseStructLinks(xMets, lookupMaps, mets.Files);

        // Parse logical structMaps into LogicalRange trees
        mets.LogicalStructures.AddRange(ParseLogicalStructMaps(xMets, lookupMaps));

        // Build map of file paths to logical ranges that reference them as whole-file fptrs
        var fileToWholeFileRanges = BuildFileToWholeFileRangesMap(mets.LogicalStructures);

        // Compute effective (inherited) metadata for all physical and logical resources
        ComputeEffectiveMetadata(mets, fileToWholeFileRanges, filesWithExplicitRights);
    }

    private void ProcessChildStructDivs(MetsFileWrapper mets, XElement parent,
        Stack<string> directoryLabels, MetsLookupMaps lookupMaps, HashSet<string> filesWithExplicitRights)
    {
        // We want to create MetsFileWrapper::PhysicalStructure (WorkingDirectories and WorkingFiles).
        // We can traverse the physical structmap, finding div type=Directory and div type=File
        // But we have a problem - if a directory has no files in it, we don't know the path of that 
        // directory. If it has grandchildren we can eventually populate it. But if not we will have
        // to rely on the AMD premis:originalName as the local path.
        foreach (var div in parent.Elements(XNames.MetsDiv))
        {
            var (accessRestrictions, rightsStatement, recordInfo, rightsExplicitlySet) = GetDmdForDiv(div, lookupMaps);

            var type = div.Attribute("TYPE")?.Value.ToLowerInvariant();
            var label = div.Attribute("LABEL")?.Value;
            if (type == "directory")
            {
                if (string.IsNullOrEmpty(label))
                {
                    throw new NotSupportedException("If a mets:div has type Directory, it must have a label");
                }

                directoryLabels.Push(label);

                
                var admId = div.Attribute("ADMID")?.Value;
                if (admId.HasText())
                {
                    // Use pre-built dictionary for O(1) lookup instead of LINQ query
                    if (lookupMaps.AmdSecMap.TryGetValue(admId, out var amd))
                    {
                        var originalName = amd.Descendants(XNames.PremisOriginalName).SingleOrDefault()?.Value;
                        Uri? storageLocation = null;
                        var storageUri = amd.Descendants(XNames.PremisContentLocation).SingleOrDefault()?.Value;
                        if (storageUri != null)
                        {
                            storageLocation = new Uri(storageUri);
                        }

                        if (originalName != null)
                        {
                            // Only in this scenario can we create a directory
                            var workingDirectory = mets.PhysicalStructure!.FindDirectory(originalName, true);
                            if (workingDirectory!.Name.IsNullOrWhiteSpace())
                            {
                                var nameFromPath = originalName.GetSlug();
                                var nameFromLabel = directoryLabels.Any() ? directoryLabels.Pop() : null;
                                workingDirectory.Name = nameFromLabel ?? nameFromPath;
                                workingDirectory.LocalPath = originalName;
                                workingDirectory.MetsExtensions = new MetsExtensions
                                {
                                    AdmId = admId,
                                    DivId = div.Attribute("ID")?.Value
                                };
                                workingDirectory.Metadata =
                                [
                                    new StorageMetadata
                                    {
                                        Source = Constants.Mets,
                                        OriginalName = originalName,
                                        StorageLocation = storageLocation
                                    }
                                ];
                                workingDirectory.AccessRestrictions = accessRestrictions;
                                workingDirectory.RightsStatement = rightsStatement;
                                workingDirectory.RecordInfo = recordInfo;
                            }
                        }
                    }
                }
                else
                {
                    // No ADMID: this is a structural-only div (e.g. PHYS_ROOT) that cannot
                    // create a WorkingDirectory. If it carries a DMDID with access/rights/
                    // recordInfo, store those on the root PhysicalStructure so that logical
                    // ranges can inherit from it (DMD_PHYS_ROOT inheritance rule).
                    if (accessRestrictions != null)
                        mets.PhysicalStructure!.AccessRestrictions ??= accessRestrictions;
                    if (rightsStatement != null)
                        mets.PhysicalStructure!.RightsStatement ??= rightsStatement;
                    if (recordInfo != null)
                        mets.PhysicalStructure!.RecordInfo ??= recordInfo;
                }
            }

            // type may be Directory, we need to match them up to file paths
            // but there might not be any directories in the structmap, just implied by flocats.

            // build all the files first on one pass then re=parse to make directories?

            bool haveUsedAdmIdAlready = false;
            foreach (var fptr in div.Elements(XNames.MetsFptr))
            {
                var admId = div.Attribute("ADMID")?.Value;
                // Goobi METS has the ADMID on the mets:div. But that means we can use it only once!
                // Going to make an assumption for now that the first encountered mets:fptr is the one that gets the ADMID
                // - this is true for Goobi at Wellcome. But in reality we'd need a stricter check than that.
                
                // In contrast, we assume (for now) that DMDID is always on the mets:div; no file itself has DMD.

                var fileId = fptr.Attribute("FILEID")!.Value;
                var fileEl = lookupMaps.FileMap[fileId];
                var mimeType =
                    fileEl.Attribute("MIMETYPE")
                        ?.Value; // Archivematica does not have this, have to get it from PRONOM, even reverse lookup
                var flocat = fileEl.Elements(XNames.MetsFLocat).Single().Attribute(XNames.XLinkHref)!.Value;
                if (admId == null)
                {
                    admId = fileEl.Attribute("ADMID")?.Value; // EPrints and Archivematica METS have ADMID on the mets:file
                    haveUsedAdmIdAlready = false;
                }

                string? digest = null;
                long size = 0;
                string? originalName = null;
                Uri? storageLocation = null;
                FileFormatMetadata? premisMetadata = null;
                VirusScanMetadata? virusScanMetadata = null;
                ExifMetadata? exifMetadata = null;
                ExtentMetadata? extentMetadata = null;
                if (!haveUsedAdmIdAlready && admId != null)
                {
                    if (!lookupMaps.TechMdMap.TryGetValue(admId, out var techMd))
                    {
                        // Archivematica does it this way - fall back to amdSec map
                        lookupMaps.AmdSecMap.TryGetValue(admId, out techMd);
                    }

                    if (techMd == null)
                    {
                        haveUsedAdmIdAlready = true;
                    }
                    else
                    {

                    var fixity = techMd.Descendants(XNames.PremisFixity).SingleOrDefault();
                    if (fixity != null)
                    {
                        var algorithm = fixity.Element(XNames.PremisMessageDigestAlgorithm)?.Value
                            .ToLowerInvariant().Replace("-", "");
                        if (algorithm == "sha256")
                        {
                            digest = fixity.Element(XNames.PremisMessageDigest)?.Value;
                        }
                    }

                    var sizeEl = techMd.Descendants(XNames.PremisSize).SingleOrDefault();
                    if (sizeEl != null)
                    {
                        long.TryParse(sizeEl.Value, out size);
                    }

                    originalName = techMd.Descendants(XNames.PremisOriginalName).SingleOrDefault()?.Value;
                    var storageUri = techMd.Descendants(XNames.PremisContentLocation).SingleOrDefault()?.Value;
                    if (storageUri != null)
                    {
                        try
                        {
                            storageLocation = new Uri(storageUri);
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "Unable to parse storage location {storageUri}", storageUri);
                        }
                    }

                    haveUsedAdmIdAlready = true;
                    var format = techMd.Descendants(XNames.PremisFormat).SingleOrDefault();
                    if (format != null)
                    {
                        var name = format.Descendants(XNames.PremisFormatName).SingleOrDefault()?.Value;
                        var key = format.Descendants(XNames.PremisFormatRegistryKey).SingleOrDefault()?.Value;
                        if (name.HasText() && key.HasText())
                        {
                            premisMetadata = new FileFormatMetadata
                            {
                                Digest = digest,
                                Source = Constants.Mets,
                                PronomKey = key,
                                FormatName = name,
                                Size = size,
                                ContentType = mimeType
                            };
                        }
                    }

                    premisMetadata ??= new FileFormatMetadata
                    {
                        Source = Constants.Mets,
                        PronomKey = "UNKNOWN",
                        FormatName = "",
                        Digest = digest
                    };

                    // Parse premis:significantProperties for extent (duration / pixel dimensions)
                    double? duration = null;
                    int? pixelWidth = null;
                    int? pixelHeight = null;
                    foreach (var sp in techMd.Descendants(XNames.PremisSignificantProperties))
                    {
                        var spType = sp.Element(XNames.PremisSignificantPropertiesType)?.Value;
                        var spValue = sp.Element(XNames.PremisSignificantPropertiesValue)?.Value;
                        if (spType == "Duration" && double.TryParse(spValue,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var dur))
                            duration = dur;
                        else if (spType == "ImageWidth" && int.TryParse(spValue, out var w))
                            pixelWidth = w;
                        else if (spType == "ImageHeight" && int.TryParse(spValue, out var h))
                            pixelHeight = h;
                    }
                    if (duration != null || pixelWidth != null || pixelHeight != null)
                    {
                        extentMetadata = new ExtentMetadata
                        {
                            Source = Constants.Mets,
                            Duration = duration,
                            PixelWidth = pixelWidth,
                            PixelHeight = pixelHeight
                        };
                    }

                    } // end else (techMd != null)
                }

                // Use pre-built dictionary for O(1) lookup instead of LINQ query
                // The digiprovMD ID contains a pattern like "digiprovmd_clamav_{admId}"
                var clamavKey = $"{Constants.VirusProvEventPrefix}{admId}";
                if (!lookupMaps.DigiprovMdMap.TryGetValue(clamavKey, out var digiprovMd))
                {
                    var lowerKey = clamavKey.ToLowerInvariant();
                    // Try case-insensitive search through the pre-built map
                    var matchingKey = lookupMaps.DigiprovMdMap.Keys
                        .FirstOrDefault(k => k.ToLower().Contains(lowerKey));
                    if (matchingKey != null)
                    {
                        digiprovMd = lookupMaps.DigiprovMdMap[matchingKey];
                    }
                }

                var virusEvent = digiprovMd?.Descendants(XNames.PremisEvent).SingleOrDefault();
                if (virusEvent != null)
                {
                    var eventDatetime = virusEvent.Descendants(XNames.PremisEventDateTime).SingleOrDefault();
                    var eventOutcomeInformation = virusEvent.Descendants(XNames.PremisEventOutcomeInformation)
                        .SingleOrDefault();

                    XElement? eventOutcomeDetailNote = null;
                    var eventOutcomeDetail = eventOutcomeInformation?.Descendants(XNames.PremisEventOutcomeDetail)
                        .SingleOrDefault();
                    if (eventOutcomeDetail != null)
                    {
                        eventOutcomeDetailNote = eventOutcomeDetail.Descendants(XNames.PremisEventOutcomeDetailNote)
                            .SingleOrDefault();
                    }

                    var eventOutcome = eventOutcomeInformation?
                        .Descendants(XNames.PremisEventOutcome)
                        .SingleOrDefault();

                    var eventDetailInformation = virusEvent
                        .Descendants(XNames.PremisEventDetailInformation)
                        .SingleOrDefault();

                    XElement? eventDetail = null;
                    if (eventDetailInformation != null)
                    {
                        eventDetail = eventDetailInformation
                            .Descendants(XNames.PremisEventDetail)
                            .SingleOrDefault();
                    }

                    virusScanMetadata = new VirusScanMetadata
                    {
                        Source = "ClamAV",
                        HasVirus = eventOutcome?.Value.ToLower() == "fail",
                        VirusFound = eventOutcomeDetailNote != null ? eventOutcomeDetailNote.Value : string.Empty,
                        Timestamp = Convert.ToDateTime(
                            eventDatetime != null ? eventDatetime.Value : DateTime.UtcNow),
                        VirusDefinition = eventDetail != null ? eventDetail.Value : string.Empty
                    };
                }
                
                
                if (admId != null && lookupMaps.AmdSecMap.TryGetValue(admId, out var amd))
                {
                    var exifMetadataNode = amd.Descendants("ExifMetadata").SingleOrDefault();
               
                    if (exifMetadataNode != null)
                    {
                        var timestamp = DateTime.UtcNow;
                        var exifMetadataList = new List<ExifTag>();
                        foreach (var element in exifMetadataNode.Descendants())
                        {
                            exifMetadataList.Add(new ExifTag{TagName = element.Name.LocalName , TagValue = element.Value });
                        }

                        exifMetadata = new ExifMetadata
                        {
                            Source = "METS",
                            Timestamp = timestamp,
                            Tags = exifMetadataList
                        };

                    }
                }

                var parts = flocat.Split('/');

                var file = new WorkingFile
                {
                    ContentType = mimeType,
                    LocalPath = flocat,
                    Digest = digest,
                    Size = size,
                    Name = label ?? parts[^1],
                    Metadata =
                    [
                        new StorageMetadata
                        {
                            Source = Constants.Mets,
                            OriginalName = originalName,
                            StorageLocation = storageLocation
                        }
                    ],
                    MetsExtensions = new MetsExtensions
                    {
                        AdmId = admId,
                        DivId = div.Attribute("ID")?.Value
                    }
                };
                if (premisMetadata != null)
                {
                    file.Metadata.Add(premisMetadata);
                }

                if (virusScanMetadata != null)
                {
                    file.Metadata.Add(virusScanMetadata);
                }

                if (exifMetadata != null)
                {
                    file.Metadata.Add(exifMetadata);
                }
                if (extentMetadata != null)
                {
                    file.Metadata.Add(extentMetadata);
                }
                file.AccessRestrictions = accessRestrictions;
                file.RightsStatement = rightsStatement;
                file.RecordInfo = recordInfo;

                if (rightsExplicitlySet)
                    filesWithExplicitRights.Add(flocat);

                mets.Files.Add(file);

                // We only know the "on disk" paths of folders from file paths in flocat
                // so if we have /folder1/folder2/folder3/file1 where folder2 has no immediate children, we never see it directly.
                // But we might see it in mets:div in the structmap
                if (parts.Length > 0)
                {
                    int walkBack = parts.Length;
                    while (walkBack > 1)
                    {
                        var parentDirectory = string.Join('/', parts[..(walkBack - 1)]);
                        var workingDirectory = mets.PhysicalStructure!.FindDirectory(parentDirectory, true);
                        if (workingDirectory!.Name.IsNullOrWhiteSpace())
                        {
                            var nameFromPath = parts[walkBack - 2];
                            var nameFromLabel = directoryLabels.Any() ? directoryLabels.Pop() : null;
                            workingDirectory.Name = nameFromLabel ?? nameFromPath;
                            workingDirectory.LocalPath = parentDirectory;
                            // This directory _may_ have physId and admId, if it is actually there in the
                            // METS structure. And if it had premis:originalName we will have already matched it.
                            // But for third party sources, how do we match it up?
                        }

                        walkBack--;
                    }
                }

            }

            ProcessChildStructDivs(mets, div, directoryLabels, lookupMaps, filesWithExplicitRights);
        }
    }


    private (List<string>?, Uri?, RecordInfo?, bool) GetDmdForDiv(XElement div, MetsLookupMaps lookupMaps)
    {
        // data gathered sparsely from optional MODS
        // We assume that the DMD is always linked from the mets:Div, never from a file
        List<string>? accessRestrictions = null;
        Uri? rightsStatement = null;
        RecordInfo? recordInfo = null;
        bool rightsExplicitlySet = false;

        var dmdId = div.Attribute("DMDID")?.Value;
        if (dmdId.HasText())
        {
            if (lookupMaps.DmdSecMap.TryGetValue(dmdId, out var dmd))
            {
                var accessConditionEls = dmd.Descendants(XNames.ModsAccessCondition);
                foreach (var accessConditionEl in accessConditionEls)
                {
                    var condType = accessConditionEl.Attribute("type")?.Value;
                    if (condType == Constants.RestrictionOnAccess || condType == "status") // "status" is Goobi access cond
                    {
                        accessRestrictions ??= [];
                        accessRestrictions.Add(accessConditionEl.Value);
                    }
                    if (accessConditionEl.Attribute("type")?.Value == Constants.UseAndReproduction)
                    {
                        // The element is present — rights was explicitly addressed in this DMDID.
                        // If the value isn't a valid URI (e.g. "null(?)"), rightsStatement stays null,
                        // but we still record that rights was explicitly set so inheritance is suppressed.
                        rightsExplicitlySet = true;
                        Uri.TryCreate(accessConditionEl.Value, UriKind.Absolute, out rightsStatement);
                    }
                }
                var recordIdentifierEls = dmd.Descendants(XNames.ModsRecordIdentifier);
                foreach (var recordIdentifierEl in recordIdentifierEls)
                {
                    recordInfo ??= new RecordInfo();
                    recordInfo.RecordIdentifiers.Add(new RecordIdentifier()
                    {
                        Source = recordIdentifierEl.Attribute("source")!.Value,
                        Value = recordIdentifierEl.Value
                    });
                }
            }
        }

        return (accessRestrictions, rightsStatement, recordInfo, rightsExplicitlySet);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // structLink parsing
    // ─────────────────────────────────────────────────────────────────────────

    private static void ParseStructLinks(XDocument xMets, MetsLookupMaps lookupMaps, List<WorkingFile> files)
    {
        var structLink = xMets.Descendants(XNames.MetsStructLink).FirstOrDefault();
        if (structLink == null) return;

        var fileByPath = files.ToDictionary(f => f.LocalPath, f => f);

        foreach (var smLink in structLink.Elements(XNames.MetsSmLink))
        {
            var fromId = smLink.Attribute(XNames.XLinkFrom)?.Value;
            var toId = smLink.Attribute(XNames.XLinkTo)?.Value;
            // xlink:arcrole — find by local name to avoid namespace ambiguity
            var arcrole = smLink.Attributes().FirstOrDefault(a => a.Name.LocalName == "arcrole")?.Value;

            if (fromId == null || toId == null) continue;

            var fromPath = lookupMaps.FileMap.TryGetValue(fromId, out var fromEl)
                ? fromEl.Elements(XNames.MetsFLocat).FirstOrDefault()?.Attribute(XNames.XLinkHref)?.Value
                : null;
            var toPath = lookupMaps.FileMap.TryGetValue(toId, out var toEl)
                ? toEl.Elements(XNames.MetsFLocat).FirstOrDefault()?.Attribute(XNames.XLinkHref)?.Value
                : null;

            if (fromPath == null || toPath == null) continue;
            if (!fileByPath.TryGetValue(fromPath, out var sourceFile)) continue;

            Uri? roleUri = arcrole != null && Uri.TryCreate(arcrole, UriKind.Absolute, out var u) ? u : null;
            sourceFile.Links.Add(new FileLink { To = toPath, Role = roleUri });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Logical structMap parsing
    // ─────────────────────────────────────────────────────────────────────────

    private List<LogicalRange> ParseLogicalStructMaps(XDocument xMets, MetsLookupMaps lookupMaps)
    {
        var result = new List<LogicalRange>();
        foreach (var sm in xMets.Descendants(XNames.MetsStructMap))
        {
            var typeAttr = sm.Attribute("TYPE")?.Value;
            if (!string.Equals(typeAttr, "logical", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var div in sm.Elements(XNames.MetsDiv))
            {
                result.Add(ParseLogicalDiv(div, lookupMaps));
            }
        }
        return result;
    }

    private LogicalRange ParseLogicalDiv(XElement div, MetsLookupMaps lookupMaps)
    {
        var id = div.Attribute("ID")?.Value ?? "";
        var type = div.Attribute("TYPE")?.Value ?? "Range";
        var label = div.Attribute("LABEL")?.Value;
        var name = GetNameFromDmd(div, lookupMaps) ?? label;

        var (accessRestrictions, rightsStatement, recordInfo, _) = GetDmdForDiv(div, lookupMaps);

        var range = new LogicalRange
        {
            Id = id,
            Type = type,
            Name = name,
            AccessRestrictions = accessRestrictions,
            RightsStatement = rightsStatement,
            RecordInfo = recordInfo
        };

        foreach (var fptr in div.Elements(XNames.MetsFptr))
        {
            var fp = ParseLogicalFptr(fptr, lookupMaps);
            if (fp != null) range.Files.Add(fp);
        }

        foreach (var childDiv in div.Elements(XNames.MetsDiv))
        {
            range.Ranges.Add(ParseLogicalDiv(childDiv, lookupMaps));
        }

        return range;
    }

    private FilePointer? ParseLogicalFptr(XElement fptr, MetsLookupMaps lookupMaps)
    {
        var areaEl = fptr.Element(XNames.MetsArea);

        if (areaEl != null)
        {
            // Area reference: time segment or image region
            var fileId = areaEl.Attribute("FILEID")?.Value ?? fptr.Attribute("FILEID")?.Value;
            if (fileId == null) return null;
            var localPath = GetLocalPathForFileId(fileId, lookupMaps);
            if (localPath == null) return null;

            var fp = new FilePointer { LocalPath = localPath };
            var betype = areaEl.Attribute("BETYPE")?.Value;
            if (string.Equals(betype, "TIME", StringComparison.OrdinalIgnoreCase))
            {
                var begin = areaEl.Attribute("BEGIN")?.Value;
                var end = areaEl.Attribute("END")?.Value;
                if (begin != null) fp.BeginTime = ParseTimeToSeconds(begin);
                if (end != null) fp.EndTime = ParseTimeToSeconds(end);
            }
            else
            {
                var coords = areaEl.Attribute("COORDS")?.Value;
                var shape = areaEl.Attribute("SHAPE")?.Value;
                fp.Region = ParseCoords(coords, shape);
            }
            return fp;
        }
        else
        {
            // Whole-file reference
            var fileId = fptr.Attribute("FILEID")?.Value;
            if (fileId == null) return null;
            var localPath = GetLocalPathForFileId(fileId, lookupMaps);
            if (localPath == null) return null;
            return new FilePointer { LocalPath = localPath };
        }
    }

    private string? GetLocalPathForFileId(string fileId, MetsLookupMaps lookupMaps)
    {
        if (!lookupMaps.FileMap.TryGetValue(fileId, out var fileEl)) return null;
        return fileEl.Elements(XNames.MetsFLocat).FirstOrDefault()?.Attribute(XNames.XLinkHref)?.Value;
    }

    private string? GetNameFromDmd(XElement div, MetsLookupMaps lookupMaps)
    {
        var dmdId = div.Attribute("DMDID")?.Value;
        if (!dmdId.HasText()) return null;
        if (!lookupMaps.DmdSecMap.TryGetValue(dmdId, out var dmd)) return null;
        return dmd.Descendants(XNames.ModsTitle).FirstOrDefault()?.Value;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Effective metadata computation
    // ─────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, List<LogicalRange>> BuildFileToWholeFileRangesMap(
        List<LogicalRange> logicalStructures)
    {
        var result = new Dictionary<string, List<LogicalRange>>();
        foreach (var range in logicalStructures)
        {
            CollectWholeFileReferences(range, result);
        }
        return result;
    }

    private static void CollectWholeFileReferences(
        LogicalRange range,
        Dictionary<string, List<LogicalRange>> result)
    {
        foreach (var fp in range.Files)
        {
            // Only whole-file references (no time segment, no image region) contribute
            if (fp.BeginTime == null && fp.EndTime == null && fp.Region == null)
            {
                if (!result.TryGetValue(fp.LocalPath, out var ranges))
                {
                    ranges = [];
                    result[fp.LocalPath] = ranges;
                }
                ranges.Add(range);
            }
        }
        foreach (var child in range.Ranges)
        {
            CollectWholeFileReferences(child, result);
        }
    }

    private static void ComputeEffectiveMetadata(
        MetsFileWrapper mets,
        Dictionary<string, List<LogicalRange>> fileToWholeFileRanges,
        HashSet<string> filesWithExplicitRights)
    {
        var physRoot = mets.PhysicalStructure!;
        var rootAccess = physRoot.AccessRestrictions ?? [];
        var rootRights = physRoot.RightsStatement;
        var rootRecordInfo = physRoot.RecordInfo;

        physRoot.EffectiveAccessRestrictions = rootAccess;
        physRoot.EffectiveRightsStatement = rootRights;
        physRoot.EffectiveRecordInfo = rootRecordInfo;

        foreach (var file in physRoot.Files)
        {
            SetFileEffective(file, rootAccess, rootRights, rootRecordInfo, fileToWholeFileRanges, filesWithExplicitRights);
        }

        foreach (var dir in physRoot.Directories)
        {
            ComputeEffectiveForDirectory(dir, rootAccess, rootRights, rootRecordInfo, fileToWholeFileRanges, filesWithExplicitRights);
        }

        // Logical ranges inherit access/rights from PHYS_ROOT only (not from objects/ or other physical divs)
        foreach (var range in mets.LogicalStructures)
        {
            ComputeEffectiveForLogicalRange(range, rootAccess, rootRights);
        }
    }

    private static void ComputeEffectiveForDirectory(
        WorkingDirectory dir,
        List<string> parentAccess,
        Uri? parentRights,
        RecordInfo? parentRecordInfo,
        Dictionary<string, List<LogicalRange>> fileToWholeFileRanges,
        HashSet<string> filesWithExplicitRights)
    {
        var effectiveAccess = dir.AccessRestrictions is { Count: > 0 } ? dir.AccessRestrictions : parentAccess;
        var effectiveRights = dir.RightsStatement ?? parentRights;
        var effectiveRecordInfo = dir.RecordInfo ?? parentRecordInfo;

        dir.EffectiveAccessRestrictions = effectiveAccess;
        dir.EffectiveRightsStatement = effectiveRights;
        dir.EffectiveRecordInfo = effectiveRecordInfo;

        foreach (var file in dir.Files)
        {
            SetFileEffective(file, effectiveAccess, effectiveRights, effectiveRecordInfo, fileToWholeFileRanges, filesWithExplicitRights);
        }

        foreach (var subDir in dir.Directories)
        {
            ComputeEffectiveForDirectory(subDir, effectiveAccess, effectiveRights, effectiveRecordInfo, fileToWholeFileRanges, filesWithExplicitRights);
        }
    }

    private static void SetFileEffective(
        WorkingFile file,
        List<string> parentAccess,
        Uri? parentRights,
        RecordInfo? parentRecordInfo,
        Dictionary<string, List<LogicalRange>> fileToWholeFileRanges,
        HashSet<string> filesWithExplicitRights)
    {
        file.EffectiveAccessRestrictions = file.AccessRestrictions is { Count: > 0 }
            ? file.AccessRestrictions
            : parentAccess;
        // If the div had an explicit use-and-reproduction element (even with an invalid value like "null(?)"),
        // use the file's own rights (which may be null) rather than inheriting from the physical parent.
        file.EffectiveRightsStatement = (file.RightsStatement != null || filesWithExplicitRights.Contains(file.LocalPath))
            ? file.RightsStatement
            : parentRights;

        // RecordInfo: own value → exactly-one whole-file logical range → physical parent
        if (file.RecordInfo != null)
        {
            file.EffectiveRecordInfo = file.RecordInfo;
        }
        else if (fileToWholeFileRanges.TryGetValue(file.LocalPath, out var ranges) && ranges.Count == 1)
        {
            file.EffectiveRecordInfo = ranges[0].RecordInfo;
        }
        else
        {
            file.EffectiveRecordInfo = parentRecordInfo;
        }
    }

    private static void ComputeEffectiveForLogicalRange(
        LogicalRange range,
        List<string> physRootAccess,
        Uri? physRootRights)
    {
        // Logical ranges inherit access/rights from PHYS_ROOT only, not from objects/ or other physical divs
        range.EffectiveAccessRestrictions = range.AccessRestrictions is { Count: > 0 }
            ? range.AccessRestrictions
            : physRootAccess;
        range.EffectiveRightsStatement = range.RightsStatement ?? physRootRights;
        range.EffectiveRecordInfo = range.RecordInfo;

        foreach (var child in range.Ranges)
        {
            ComputeEffectiveForLogicalRange(child, physRootAccess, physRootRights);
        }
    }

    private static double ParseTimeToSeconds(string time)
    {
        var parts = time.Split(':');
        if (parts.Length == 3)
        {
            var h = double.TryParse(parts[0], out var hh) ? hh : 0;
            var m = double.TryParse(parts[1], out var mm) ? mm : 0;
            var s = double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ss) ? ss : 0;
            return h * 3600 + m * 60 + s;
        }
        return 0;
    }

    private static Rectangle? ParseCoords(string? coords, string? shape)
    {
        if (!string.Equals(shape, "RECT", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(coords))
            return null;
        var parts = coords.Split(',');
        if (parts.Length == 4 &&
            int.TryParse(parts[0].Trim(), out var x1) &&
            int.TryParse(parts[1].Trim(), out var y1) &&
            int.TryParse(parts[2].Trim(), out var x2) &&
            int.TryParse(parts[3].Trim(), out var y2))
        {
            return new Rectangle { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
        }
        return null;
    }

}