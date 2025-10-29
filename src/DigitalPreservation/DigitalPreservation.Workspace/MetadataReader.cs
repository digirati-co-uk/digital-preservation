using DigitalPreservation.Common.Model.ToolOutput.Siegfried;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using Storage.Repository.Common;
using System.Diagnostics;

namespace DigitalPreservation.Workspace;

public class MetadataReader : IMetadataReader
{
    private readonly IStorage storage;
    private readonly Uri rootUri;
    private Uri workingRootUri;

    private Dictionary<string, string>? bagItSha256Values;
    private SiegfriedOutput? siegfriedSiegfriedOutput;
    private SiegfriedOutput? brunnhildeSiegfriedOutput;
    private List<VirusModel> infectedFiles = [];
    
    private readonly Dictionary<string, List<Metadata>> metadataByFiles =  new();

    public static async Task<MetadataReader> Create(IStorage storage, Uri rootUri)
    {
        var reader = new MetadataReader(storage, rootUri);
        await reader.FindMetadata();
        return reader;
    }

    private MetadataReader(IStorage storage, Uri rootUri)
    {
        this.storage = storage;
        this.rootUri = rootUri;
        workingRootUri = rootUri;
    }

    private async Task FindMetadata()
    {
        var timestamp = DateTime.UtcNow;
        bool isBagItLayout = false;
        // use storage to read and parse all sources of metadata
        var bagitSha256ManifestResult = await storage.GetStream(rootUri.AppendEscapedSlug("manifest-sha256.txt"));
        if (bagitSha256ManifestResult is { Success: true, Value: not null })
        {
            isBagItLayout = true;
            await ReadBagItSha256(bagitSha256ManifestResult.Value);
            // Look for other bagit manifests? (non sha-256)
        }
        // It still might be a bagit layout but without any root bagit files yet
        var bagItRoot = FolderNames.GetFilesLocation(rootUri, isBagItLayout: true);
        if (!isBagItLayout)
        {
            isBagItLayout = await storage.Exists(bagItRoot);
        }
        workingRootUri = isBagItLayout ? bagItRoot : rootUri;
        
        var siegfriedUri = await FindSiegfriedOutput();
        if (siegfriedUri is not null)
        {
            siegfriedSiegfriedOutput = await ParseSiegfriedOutput(siegfriedUri);
        }

        // take care - in S3 brunnhildeRoot does not exist!
        // need to probe for an actual file, not a directory
        var brunnhildeRoot = workingRootUri.AppendEscapedSlug("metadata").AppendEscapedSlug("brunnhilde");
        var brunnhildeProbe = brunnhildeRoot.AppendEscapedSlug("report.html"); // could use a different probe perhaps
        if (await storage.Exists(brunnhildeProbe))
        {
            brunnhildeSiegfriedOutput = await ParseSiegfriedOutput(brunnhildeRoot.AppendEscapedSlug("siegfried.csv"));
            var brunnhildeAVResult = await storage.GetStream(brunnhildeRoot.AppendEscapedSlug("logs").AppendEscapedSlug("viruscheck-log.txt"));
            if (brunnhildeAVResult is { Success: true, Value: not null })
            {
                infectedFiles = await ReadInfectedFilePaths(brunnhildeAVResult.Value);
            }
            var brunnhildeHtmlResult = await storage.GetStream(brunnhildeRoot.AppendEscapedSlug("report.html"));
            if (brunnhildeHtmlResult is { Success: true, Value: not null })
            {
                var brunnhildeHtml = await GetTextFromStream(brunnhildeHtmlResult.Value);
                GetMetadataList("metadata/brunnhilde/report.html").Add(
                    new ToolOutput
                    {
                        Source = "Brunnhilde",
                        Timestamp = timestamp,
                        Content = brunnhildeHtml,
                        ContentType = "text/html"
                    });
            }
        }
        
        // when parsing files with paths, find the common origin and look for objects/ and metadata/
        string? bagItCommonParent;
        if (bagItSha256Values is { Count: > 0 })
        {
            // Bagit paths are made AFTER bagging, example:
            // data/objects/nyc/DSCF0969.JPG
            bagItCommonParent = StringUtils.GetCommonParent(bagItSha256Values.Keys);
            // => data/objects
            bagItCommonParent = AllowForObjectsAndMetadata(bagItCommonParent);
            // => data
            foreach (var kvp in bagItSha256Values)
            {
                var localPath = kvp.Key.RemoveStart(bagItCommonParent).RemoveStart("/");
                var metadataList = GetMetadataList(localPath!);
                metadataList.Add(new DigestMetadata
                {
                    Source = "BagIt",
                    Digest = kvp.Value.ToLowerInvariant(),
                    Timestamp = timestamp
                });
            }
        }
        string? siegfriedSiegfriedCommonParent;
        if (siegfriedSiegfriedOutput is { Files.Count: > 0 })
        {
            // Siegfried paths will probably have the item folder at their root, example:
            // bc-example-1/objects/nyc/DSCF0969.JPG
            siegfriedSiegfriedCommonParent = StringUtils.GetCommonParent(siegfriedSiegfriedOutput.Files.Select(f => f.Filename!));
            // => bc-example-1/objects
            siegfriedSiegfriedCommonParent = AllowForObjectsAndMetadata(siegfriedSiegfriedCommonParent);
            // => bc-example-1
            AddFileFormatMetadata(siegfriedSiegfriedOutput, siegfriedSiegfriedCommonParent, "Siegfried", timestamp);
        }
        string? brunnhildeSiegfriedCommonParent;
        if (brunnhildeSiegfriedOutput is { Files.Count: > 0 })
        {
            // Siegfried via Brunnhilde might be a full path, example:
            // /home/tomcrane/__packing_area/bc-example-1/objects/nyc/DSCF0969.JPG
            brunnhildeSiegfriedCommonParent = StringUtils.GetCommonParent(brunnhildeSiegfriedOutput.Files.Select(f => f.Filename!));
            // => /home/tomcrane/__packing_area/bc-example-1/objects
            brunnhildeSiegfriedCommonParent = AllowForObjectsAndMetadata(brunnhildeSiegfriedCommonParent);
            // => /home/tomcrane/__packing_area/bc-example-1
            // => bc-example-1
            AddFileFormatMetadata(brunnhildeSiegfriedOutput, brunnhildeSiegfriedCommonParent, "Brunnhilde", timestamp);
        }
        string? brunnhildeAvCommonPrefix;
        if (infectedFiles.Count > 0)
        {
            var virusDefinitionResult = await storage.GetStream(rootUri.AppendEscapedSlug("virus-definition.txt"));
            var virusDefinition = string.Empty;
            if (virusDefinitionResult is { Success: true, Value: not null })
            {
                virusDefinition = await GetVirusDefinition(virusDefinitionResult.Value);
            }
            // the parent of the first instance of /metadata or /metadata/
            // the parent of the first instance of /objects or /objects/
            brunnhildeAvCommonPrefix = StringUtils.GetCommonParent(infectedFiles.Select(s => s.Filepath));
            brunnhildeAvCommonPrefix = AllowForObjectsAndMetadata(brunnhildeAvCommonPrefix);
            AddVirusScanMetadata(infectedFiles, brunnhildeAvCommonPrefix, "ClamAv", timestamp, virusDefinition);
        }

        
    }

    private void AddFileFormatMetadata(SiegfriedOutput siegfriedOutput, string commonParent, string source, DateTime timestamp)
    {
        foreach (var file in siegfriedOutput.Files)
        {
            if (file.Matches.Count != 1)
            {
                continue;
            }
            var localPath = file.Filename.RemoveStart(commonParent).RemoveStart("/");
            var metadataList = GetMetadataList(localPath!);
            metadataList.Add(new FileFormatMetadata
            {
                Source = source,
                Digest = file.Sha256?.ToLowerInvariant(),
                Size = file.Filesize,
                PronomKey = file.Matches[0].Id,
                FormatName = file.Matches[0].Format,
                ContentType = file.Matches[0].Mime,
                Timestamp = timestamp
            });
        }
    }

    private void AddVirusScanMetadata(List<VirusModel> infectedFiles, string commonParent, string source, DateTime timestamp, string virusDefinition)
    {
        // this is the new method
        //TODO: source ClamAv
        foreach (var file in infectedFiles)
        {
            var localPath = file.Filepath.RemoveStart(commonParent).RemoveStart("/"); // check this!
            var metadataList = GetMetadataList(localPath!);
            metadataList.Add(new VirusScanMetadata
            {
                Source = source,
                HasVirus = true,
                VirusFound = file.VirusFound,
                Timestamp = timestamp,
                VirusDefinition = virusDefinition
            });
        }
    }

    private async Task<string> GetVirusDefinition(Stream stream)
    {
        return await GetTextFromStream(stream);
    }

    private List<Metadata> GetMetadataList(string localPath)
    {
        if (metadataByFiles.TryGetValue(localPath, out var metadataList))
        {
            return metadataList;
        }
        metadataList = [];
        metadataByFiles[localPath] = metadataList;

        return metadataList;
    }

    private string AllowForObjectsAndMetadata(string naiveCommonParent)
    {
        // What do we do about things like
        //   /some/path/objects/blah/objects/foo
        //   /some/path/objects/blah/objects/bar
        // ... where we really want /some/path even though /some/path/objects/blah is the dumb answer
        // and also theoretical
        //   objects/foo
        //   objects/bar
        // and the combination
        //   objects/blah/objects/foo
        //   objects/blah/objects/bar
        // and things like
        //   objects/images/1
        //   objects/images/2
        var startsSlash = naiveCommonParent.StartsWith('/');
        var forcedRoot = startsSlash ? naiveCommonParent : $"/{naiveCommonParent}";
        if (ExtractObjectsIndex("objects", out var adjustedForObjects))
        {
            return adjustedForObjects;
        }
        if (ExtractObjectsIndex("metadata", out var adjustedForMetadata))
        {
            // It would be unusual for a deposit to have metadata but not objects. But just in case
            return adjustedForMetadata;
        }

        return naiveCommonParent;
        
        bool ExtractObjectsIndex(string testDir, out string s)
        {
            var index = forcedRoot.IndexOf($"/{testDir}/", StringComparison.InvariantCulture);
            if (index == -1 && forcedRoot.EndsWith($"/{testDir}"))
            {
                index = forcedRoot.Length - (testDir.Length + 1);
            }

            if (index != -1)
            {
                var actualIndex = startsSlash ? index : index - 1;
                s = naiveCommonParent[..actualIndex];
                return true;
            }
            s = string.Empty;
            return false;
        }
    }

    private async Task<List<VirusModel>> ReadInfectedFilePaths(Stream stream)
    {
        var txt = await GetTextFromStream(stream);
        var result = ConvertClamResultStringToJson(txt);
        var model = new List<VirusModel>();
        foreach (var fileVirus in result.Hits)
        {
            var virusStringSplit = fileVirus.Split(':');
            model.Add(new VirusModel
            {
                Filepath = virusStringSplit[0],
                VirusFound = virusStringSplit[1]
            });
        }
        return model;
    }

    public static ClamScanResult ConvertClamResultStringToJson(string clamResultStr)
    {
        // Split the string by newlines and filter out any empty entries
        var clamResultList = clamResultStr.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).ToList();

        // Find the index of the scan summary marker
        int resultsMarker = clamResultList.IndexOf("----------- SCAN SUMMARY -----------");

        if (resultsMarker == -1)
        {
            // Return an empty result if the marker is not found
            return new ClamScanResult { Hits = [], Summary = [] };
        }

        // Get the hits (lines before the marker)
        var hitList = clamResultList.Take(resultsMarker).ToList();

        // Get the summary (lines after the marker)
        var summaryList = clamResultList.Skip(resultsMarker + 1).ToList();

        return new ClamScanResult { Hits = hitList, Summary = summaryList };
    }

    private async Task<SiegfriedOutput?> ParseSiegfriedOutput(Uri siegfriedUri)
    {
        var streamResult = await storage.GetStream(siegfriedUri);
        if (streamResult is not { Success: true, Value: not null })
        {
            return null;
        }
        var txt = await GetTextFromStream(streamResult.Value);

        // first assume that the extension corresponds to the data - it might not!
        // (if siegfried used without format and > output.xxx being consistent)
        if (siegfriedUri.ToString().ToLowerInvariant().EndsWith("csv"))
        {
            try
            {
                var output = SiegfriedOutput.FromCsvString(txt);
                if (output.Files.Any())
                {
                    return output;
                }
            }
            catch
            {
                // ignored
            }
        }
        
        // Now try the YAML variant
        try
        {
            var output = SiegfriedOutput.FromYamlString(txt);
            if (output.Files.Any())
            {
                return output;
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private async Task<Uri?> FindSiegfriedOutput()
    {
        // Allow as much leeway as possible without going over the top
        // find {}/metadata/siegfried/siegfried.yml
        // find {}/metadata/siegfried/siegfried.yaml
        // find {}/metadata/siegfried/siegfried.csv
        var listing = await storage.GetListing(workingRootUri, "/metadata/siegfried/");
        var candidateStrings = listing
            .Select(u => u.ToString().ToLowerInvariant())
            .Where(s => s.EndsWith(".yaml") || s.EndsWith(".yml") || s.EndsWith(".csv"))
            .ToList();
        if (candidateStrings.Count == 0)
        {
            return null;
        }

        if (candidateStrings.Count > 1)
        {
            candidateStrings.RemoveAll(s => !s.Split('/')[0].StartsWith("siegfried."));
        }
        
        // ideally, this is one of the three above:
        return new Uri(candidateStrings[0]);
        
    }

    private async Task ReadBagItSha256(Stream stream)
    {
        var txt = await GetTextFromStream(stream);
        bagItSha256Values = new Dictionary<string, string>();
        foreach (var line in txt.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                bagItSha256Values.Add(parts[1], parts[0]);
            }
        }
    }

    private static async Task<string> GetTextFromStream(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var txt = await reader.ReadToEndAsync();
        return txt;
    }

    public void Decorate(WorkingBase workingBase)
    {
        // look up in the built sources any metadata you have for this file or folder
        var adjustedContentLocalPath = FolderNames.RemovePathPrefix(workingBase.LocalPath);
        if (adjustedContentLocalPath.IsNullOrWhiteSpace())
        {
            return;
        }
        if (metadataByFiles.TryGetValue(adjustedContentLocalPath, out var metadataList))
        {
            workingBase.Metadata = metadataList;
        }
    }
}

// Define a class to represent the JSON output structure
public class ClamScanResult
{
    public List<string> Hits { get; set; }
    public List<string> Summary { get; set; }
}

public class VirusModel
{
    public string Filepath { get; set; }
    public string VirusFound { get; set; }
}