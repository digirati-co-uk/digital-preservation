using System.Text;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.ToolOutput.Siegfried;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using Storage.Repository.Common;
using System.Text.RegularExpressions;
using File = DigitalPreservation.Common.Model.ToolOutput.Siegfried.File;
using StringUtils = DigitalPreservation.Utils.StringUtils;

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
    private List<ExifModel> exifMetadataList = [];
    
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
        var brunnhildeAVResult = await storage.GetStream(brunnhildeRoot.AppendEscapedSlug("logs").AppendEscapedSlug("viruscheck-log.txt"));

        if (await storage.Exists(brunnhildeProbe))
        {
            brunnhildeSiegfriedOutput = await ParseSiegfriedOutput(brunnhildeRoot.AppendEscapedSlug("siegfried.csv"));
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
        var brunnhildeSiegfriedCommonParent = string.Empty;
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

        var virusDefinitionRoot = workingRootUri.AppendEscapedSlug("metadata").AppendEscapedSlug("virus-definition").AppendEscapedSlug("virus-definition.txt");

        var virusDefinition = string.Empty;
        var virusDefinitionFileExists = await storage.Exists(virusDefinitionRoot);
        if (virusDefinitionFileExists && brunnhildeAVResult is { Success: true, Value: not null })
        {
            var virusDefinitionResult = await storage.GetStream(virusDefinitionRoot);
            if (virusDefinitionResult is { Success: true, Value: not null })
            {
                virusDefinition = await GetTextFromStream(virusDefinitionResult.Value);
            }
        }

        if (!virusDefinitionFileExists || string.IsNullOrEmpty(virusDefinition))
        {
            var brunnhildeVirusLogResult = await storage.GetStream(brunnhildeRoot.AppendEscapedSlug("logs").AppendEscapedSlug("viruscheck-log.txt"));

            if (brunnhildeVirusLogResult is { Success: true, Value: not null })
            {
                virusDefinition = await GetVirusDefinitionFromBrunnhilde(brunnhildeVirusLogResult.Value);
            }
        }

        var brunnhildeCommonPrefix = StringUtils.GetCommonParent(infectedFiles.Select(s => s.Filepath));
        brunnhildeCommonPrefix = AllowForObjectsAndMetadata(brunnhildeCommonPrefix);

        var brunnhildeFiles = brunnhildeSiegfriedOutput is { Files.Count: > 0 } ? brunnhildeSiegfriedOutput.Files : [];
        AddVirusScanMetadata(brunnhildeFiles, brunnhildeCommonPrefix, brunnhildeSiegfriedCommonParent, "ClamAv", timestamp, virusDefinition);

        exifMetadataList = await GetExifOutputForAllFiles();

        if (!exifMetadataList.Any())
        {
            AddMetadataToolOutput(timestamp);
            return;
        }

        brunnhildeCommonPrefix = StringUtils.GetCommonParent(exifMetadataList.Select(s => s.Filepath));
        brunnhildeCommonPrefix = AllowForObjectsAndMetadata(brunnhildeCommonPrefix);
        AddExifMetadata(brunnhildeCommonPrefix, "ExifTool", timestamp);

        AddMetadataToolOutput(timestamp);
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

    private void AddVirusScanMetadata(List<File> objectsFiles, string infectedFilesCommonParent, string siegfriedFilesCommonParent, string source,
        DateTime timestamp, string virusDefinition)
    {
        var infectedLocalPaths = new List<string>();
        foreach (var file in infectedFiles)
        {
            var localPath = file.Filepath.RemoveStart(infectedFilesCommonParent).RemoveStart("/"); // check this!
            if (localPath != null)
            {
                infectedLocalPaths.Add(localPath);
            }

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

        foreach (var file in objectsFiles)
        {
            var alreadyVirusScanMetadata = false;
            if (infectedLocalPaths.Count > 0)
            {
                alreadyVirusScanMetadata = infectedLocalPaths.Any(s => file.Filename != null && file.Filename.Contains(s));
            }

            if (alreadyVirusScanMetadata)
                continue;

            var localPath = file.Filename.RemoveStart(siegfriedFilesCommonParent).RemoveStart("/"); // check this!

            var metadataList = GetMetadataList(localPath!);
            metadataList.Add(new VirusScanMetadata
            {
                Source = source,
                HasVirus = false,
                VirusFound = string.Empty,
                Timestamp = timestamp,
                VirusDefinition = virusDefinition
            });
        }
    }

    private void AddExifMetadata(string exifFilesCommonParent, string source, DateTime timestamp)
    {
        foreach (var exifMetadata in exifMetadataList)
        {
            var localPath = exifMetadata.Filepath.RemoveStart(exifFilesCommonParent).RemoveStart("/"); // check this!

            var metadataList = GetMetadataList(localPath!);
            metadataList.Add(new ExifMetadata
            {
                Source = source,
                Timestamp = timestamp,
                Tags = exifMetadata.ExifMetadata
            });

        }
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
        var result = await GetClamScanOutput(stream);
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

    private async Task<ClamScanResult> GetClamScanOutput(Stream stream)
    {
        var txt = await GetTextFromStream(stream);
        var result = ConvertClamResultStringToJson(txt);
        return result;
    }

    public static ClamScanResult ConvertClamResultStringToJson(string clamResultStr)
    {
        // Split the string by newlines and filter out any empty entries
        var clamResultList = clamResultStr.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).ToList();

        // Find the index of the scan summary marker
        var resultsMarker = clamResultList.IndexOf("----------- SCAN SUMMARY -----------");

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

    private async Task<string> GetVirusDefinitionFromBrunnhilde(Stream stream)
    {
        var result = await GetClamScanOutput(stream);
        var knownViruses = string.Empty;
        var engineVersion = string.Empty;

        foreach (var summaryInfo in result.Summary)
        {
            var summaryStringSplit = summaryInfo.Split(':');

            if (summaryStringSplit[0].ToLower() == "known viruses")
                knownViruses = summaryStringSplit[1];
            if (summaryStringSplit[0].ToLower() == "engine version")
                engineVersion = summaryStringSplit[1];
        }

        return $"ClamAV {engineVersion} Known viruses {knownViruses} ";
    }

    private async Task<List<ExifModel>> GetExifOutputForAllFiles()
    {
        var metadataRoot = workingRootUri.AppendEscapedSlug("metadata");
        var exifResult = await storage.GetStream(metadataRoot.AppendEscapedSlug("exif").AppendEscapedSlug("exif_output.txt"));

        if (exifResult.Value == null) return [];
        var txt = await GetTextFromStream(exifResult.Value);
        return ParseExifToolOutput(txt);

    }

    public static List<ExifModel> ParseExifToolOutput(string exifResultStr)
    {
        try
        {
            var exifResultList = exifResultStr.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).ToList();
            var result = new List<ExifModel>();

            var exifModel = new ExifModel { Filepath = string.Empty, ExifMetadata = [] };
            var i = 0;
            var exifMetadataForFile = new List<ExifTag>();

            var sbRawOutput = new StringBuilder();

            foreach (var str in exifResultList)
            {
                if (str.Contains("========") || str.Contains("directories scanned"))
                {
                    if (i > 0)
                    {
                        exifModel.ExifMetadata = new List<ExifTag>(exifMetadataForFile);
                        exifModel.ExifMetadata.Add(new ExifTag
                        {
                            TagName = "RawOutput",
                            TagValue = sbRawOutput.ToString()
                        });

                        exifMetadataForFile.Clear();
                        sbRawOutput.Clear();
                        result.Add(exifModel);

                        if (str.Contains("directories scanned"))
                            break;

                    }

                    var fileName = str.Replace("========", string.Empty).Trim();
                    exifModel = new ExifModel { Filepath = fileName };

                    i++;
                }
                else
                {
                    sbRawOutput.Append(str);
                    sbRawOutput.Append("\\n");

                    var metadataPair = str.Split(":", 2);

                    var rgx = new Regex("[^a-zA-Z0-9]");
                    var key = rgx.Replace(metadataPair[0].Trim(), "");

                    exifMetadataForFile.Add(new ExifTag
                    {
                        TagName = key,
                        TagValue = metadataPair[1].Trim()
                    });
                }
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }

    }

    private void SetMetadataHtml(List<Metadata>? metadataList, ExifModel? exifMetadata, DateTime timestamp)
    {
        if (metadataList == null) return;
        var brunnhildeMetadata = metadataList.FirstOrDefault(x => x.Source.ToLower() == "brunnhilde");
        var clamMetadata = metadataList.FirstOrDefault(x => x.Source.ToLower() == "clamav");

        IDictionary<string, string>? brunnhildeMetadataDictionary = null;
        IDictionary<string, string>? clamMetadataDictionary = null;

        if (brunnhildeMetadata is not null)
            brunnhildeMetadataDictionary = CollectionUtils.GetValues(brunnhildeMetadata);


        if (clamMetadata is not null)
            clamMetadataDictionary = CollectionUtils.GetValues(clamMetadata);

        IEnumerable<string>? brunnhildeStrings;
        var brunnhildeHtml = string.Empty;
        IEnumerable<string>? clamStrings;
        var clamHtml = string.Empty;

        brunnhildeStrings = brunnhildeMetadataDictionary?.Select(x => $"{x.Key}: {x.Value}");
        if (brunnhildeStrings is not null) 
            brunnhildeHtml = string.Join("<br>", brunnhildeStrings);

        clamStrings = clamMetadataDictionary?.Select(x => $"{x.Key}: {x.Value}");
        if (clamStrings is not null) 
            clamHtml = string.Join("<br>", clamStrings);

        var rawOutput = exifMetadata?.ExifMetadata.FirstOrDefault(x => x.TagName?.ToLower() == "rawoutput")?.TagValue;
        var replaceNewLineRawOutput = rawOutput?.Replace("\\n", "<br>");

        var htmlBody = new StringBuilder();

        if (!string.IsNullOrEmpty(brunnhildeHtml))
            htmlBody.Append($@"<h1>Brunnhilde</h1>
                           <pre>{brunnhildeHtml}</pre>");

        if (!string.IsNullOrEmpty(brunnhildeHtml))
            htmlBody.Append($@"<h1>ClamAV</h1>
                           <pre>{clamHtml}</pre>");

        if (!string.IsNullOrEmpty(replaceNewLineRawOutput))
            htmlBody.Append($@"<h1>Exif Metadata</h1>
                           <pre>{replaceNewLineRawOutput}</pre>");

        metadataList.Add(new ToolOutput
        {
            Source = "Exif",
            Timestamp = timestamp,
            Content = $@"<html>
                            {GetStyle()}
                            <body>
                                {htmlBody}
                            </body>
                        </html>",
            ContentType = "text/html"
        });

        htmlBody.Clear();
    }

    private string GetStyle()
    {
        return @"
            <head>
            <title>Metadata report</title>
            <meta charset=""utf-8"">
            <style type=""text/css"">
            body {
              font-family: Arial, Helvetica, sans-serif;
              margin: 20px 20px 20px 20px;
              padding: 10px;
              width: 95%;
            }

            header {
              position: fixed;
              top: 0;
              width: 95%;
              padding-bottom: 10px;
              border-bottom: 2px solid #000;
              background: white;
              z-index: 1001;
            }

            h1 {
              margin-bottom: 10px;
            }

            nav a {
              padding-right: 10px;
            }

            nav a:not(:first-child) {
              padding-left: 10px;
            }

            nav a:not(:last-child) {
              border-right: 1px solid #ddd;
            }

            div {
              padding-bottom: 10px;
            }

            table {
              border-collapse: collapse;
            }

            td {
              border-bottom: 1px solid #ddd;
              padding: 10px 20px 6px 20px;
            }

            td:not(:last-child) {
              border-right: 1px solid #ddd;
            }

            th {
              border-bottom: 2px solid #ddd;
              padding: 10px 20px 6px 20px;
              background-color: #f5f5f5;
            }

            th:not(:last-child) {
              border-right: 1px solid #ddd;
            }

            tr:hover {
              background-color: #f5f5f5;
            }

            a {
              color: #007BFF;
              text-decoration: none;
            }

            a.anchor {
              display: block;
              position: relative;
              top: -120px;
              visibility: hidden;
            }

            hr {
              width: 25%;
              color: #ddd;
              text-align: left;
              margin-left: 0;
            }

            .hidden {
              display: none;
            }
            </style>
            </head>";
    }

    private void AddMetadataToolOutput(DateTime timestamp)
    {
        string brunnhildeSiegfriedCommonParent;
        if (brunnhildeSiegfriedOutput is { Files.Count: > 0 })
        {
            brunnhildeSiegfriedCommonParent = StringUtils.GetCommonParent(brunnhildeSiegfriedOutput.Files.Select(f => f.Filename!));
            brunnhildeSiegfriedCommonParent = AllowForObjectsAndMetadata(brunnhildeSiegfriedCommonParent);
            foreach (var file in brunnhildeSiegfriedOutput.Files)
            {
                var localPath = file.Filename.RemoveStart(brunnhildeSiegfriedCommonParent).RemoveStart("/");
                var metadataList = GetMetadataList(localPath!);
                var exifMetadata = !string.IsNullOrEmpty(localPath) ?  exifMetadataList.FirstOrDefault(x => x.Filepath.Contains(localPath)) : null;

                SetMetadataHtml(metadataList, exifMetadata, timestamp);
            }
        }
    }
}

// Define a class to represent the JSON output structure
public class ClamScanResult
{
    public required List<string> Hits { get; set; }
    public required List<string> Summary { get; set; }
}

public class VirusModel
{
    public required string Filepath { get; set; }
    public required string VirusFound { get; set; }
}

public class ExifModel
{
    public required string Filepath { get; set; }
    public List<ExifTag> ExifMetadata { get; set; } = [];
}