using System.Text.Json.Serialization;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public class MovingDirectory : MovingBase
{
    [JsonPropertyName("files")]
    [JsonPropertyOrder(5)]
    public List<MovingFile> Files { get; set; } = [];
    
    [JsonPropertyName("directories")]
    [JsonPropertyOrder(6)]
    public List<MovingDirectory> Directories { get; set; } = [];

    public MovingDirectory FindDirectory(string? path, bool create = false)
    {
        if (path.IsNullOrWhiteSpace() || path == "/")
        {
            return this;
        }
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var directory = this;
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (create)
            {
                var potentialDirectory = directory.Directories.SingleOrDefault(d => d.GetSlug() == part);
                if (potentialDirectory == null)
                {
                    potentialDirectory = new MovingDirectory { LocalPath = string.Join('/', parts.Take(index + 1)) };
                    directory.Directories.Add(potentialDirectory);
                }
                directory = potentialDirectory;
            }
            else
            {
                directory = directory.Directories.Single(d => d.GetSlug() == part);
            }
        }

        return directory;
    }
}