using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DigitalPreservation.Common.Model.ToolOutput.Siegfried;

public class Output
{
    public TechnicalProvenance? TechnicalProvenance { get; set; }
    public List<File> Files { get; } = [];


    public static Output FromStringReader(StringReader reader)
    {
        var parser = new Parser(reader);
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        // Consume the stream start event "manually"
        parser.Expect<StreamStart>();

        var output = new Output();
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
            output.Files.Add(file);
        }
        return output;
    }

    public static Output FromString(string input)
    {
        var reader = new StringReader(input);
        return FromStringReader(reader);
    }
}