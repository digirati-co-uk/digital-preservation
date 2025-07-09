using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using DigitalPreservation.Utils;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DigitalPreservation.Common.Model.ToolOutput.Siegfried;

public class SiegfriedOutput
{
    public TechnicalProvenance? TechnicalProvenance { get; set; }
    public List<File> Files { get; set; } = [];


    public static SiegfriedOutput FromYamlStringReader(StringReader reader)
    {
        var parser = new Parser(reader);
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        // Consume the stream start event "manually"
        parser.Expect<StreamStart>();

        var output = new SiegfriedOutput();
        bool first = true;
        while (parser.Accept<DocumentStart>())
        {
            if (first)
            {
                output.TechnicalProvenance = deserializer.Deserialize<TechnicalProvenance>(parser);
                first = false;
                continue;
            }
            
            var file = deserializer.Deserialize<File>(parser);
            NormaliseFileSeparators(file);
            output.Files.Add(file);
        }
        return output;
    }

    private static void NormaliseFileSeparators(File file)
    {
        if (file.Filename.HasText() && file.Filename.Contains('\\'))
        {
            // Siegfried may have been run on Windows
            file.Filename = file.Filename.Replace('\\', '/');
        }
    }

    public static SiegfriedOutput FromYamlString(string input)
    {
        var reader = new StringReader(input);
        return FromYamlStringReader(reader);
    }
    
    public static SiegfriedOutput FromCsvString(string input)
    {
        // For starters assume zero or one matches per file in the CSV output
        using var reader = new StringReader(input);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<SiegfriedCsvRowMap>();
        var records = csv.GetRecords<SiegfriedCsvRow>();
        var files = records.Select(r => r.ToFile()).ToList();
        foreach (var file in files)
        {
            NormaliseFileSeparators(file);
        }
        return new SiegfriedOutput { Files = files };
    }
}

internal class SiegfriedCsvRow
{
    public string? Filename { get; set; }
    public long? Filesize { get; set; }
    public DateTime? Modified { get; set; }
    public string? Errors { get; set; }
    public string? Sha256 { get; set; }
    public string? Namespace { get; set; }
    public string? Id { get; set; }
    public string? Format { get; set; }
    public string? Version { get; set; }
    public string? Mime { get; set; }
    public string? Class { get; set; }
    public string? Basis { get; set; }
    public string? Warning { get; set; }

    public File ToFile()
    {
        var file = new File
        {
            Filename = Filename.HasText() ? Filename : null,
            Filesize = Filesize,
            Modified = Modified,
            Errors = Errors.HasText() ? Errors : null,
            Sha256 = Sha256.HasText() ? Sha256 : null
        };
        if (Id != null)
        {
            file.Matches = [
                new Match
                {
                    Ns = Namespace,
                    Id = Id,
                    Format = Format,
                    Version = Version,
                    Mime = Mime.HasText() ? Mime : null,
                    Class = Class.HasText() ? Class : null,
                    Basis = Basis.HasText() ? Basis : null,
                    Warning = Warning.HasText() ? Warning : null
                }
            ];
        }

        return file;
    }
}

internal sealed class SiegfriedCsvRowMap : ClassMap<SiegfriedCsvRow>
{
    public SiegfriedCsvRowMap()
    {
        Map(m => m.Filename).Name("filename");
        Map(m => m.Filesize).Name("filesize");
        Map(m => m.Modified).Name("modified");
        Map(m => m.Errors).Name("errors");
        Map(m => m.Sha256).Name("sha256");
        Map(m => m.Namespace).Name("namespace");
        Map(m => m.Id).Name("id");
        Map(m => m.Format).Name("format");
        Map(m => m.Version).Name("version");
        Map(m => m.Mime).Name("mime");
        Map(m => m.Class).Name("class");
        Map(m => m.Basis).Name("basis");
        Map(m => m.Warning).Name("warning");
    }
}