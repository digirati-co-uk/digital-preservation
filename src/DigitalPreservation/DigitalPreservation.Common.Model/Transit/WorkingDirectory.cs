using System.Text.Json.Serialization;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public class WorkingDirectory : WorkingBase
{
    [JsonPropertyName("files")]
    [JsonPropertyOrder(5)]
    public List<WorkingFile> Files { get; set; } = [];
    
    [JsonPropertyName("directories")]
    [JsonPropertyOrder(6)]
    public List<WorkingDirectory> Directories { get; set; } = [];

    public WorkingDirectory? FindDirectory(string? path, bool create = false)
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
            var potentialDirectory = directory.Directories.SingleOrDefault(d => d.GetSlug() == part);
            if (create)
            {
                if (potentialDirectory == null)
                {
                    potentialDirectory = new WorkingDirectory { LocalPath = string.Join('/', parts.Take(index + 1)) };
                    directory.Directories.Add(potentialDirectory);
                }
            }
            else
            {
                if (potentialDirectory == null)
                {
                    return null;
                }
            }

            directory = potentialDirectory;
        }

        return directory;
    }
    
    
    public Container ToContainer(Uri repositoryUri, Uri origin)
    {
        var container = new Container
        {
            Name = Name,
            Id = repositoryUri,
            Origin = origin
        };
        foreach (var wd in Directories)
        {
            var slug = wd.GetSlug();
            container.Containers.Add(wd.ToContainer(repositoryUri.AppendSlug(slug), origin.AppendSlug(slug)));
        }
        foreach (var wf in Files)
        {
            var slug = wf.GetSlug();
            container.Binaries.Add(new Binary
            {
                Id = repositoryUri.AppendSlug(slug),
                Name = wf.Name,
                ContentType = wf.ContentType,
                Digest = wf.Digest,
                Size = wf.Size ?? 0,
                Origin = origin.AppendSlug(slug)
            });
        }
        return container;
    }

    public int DescendantFileCount(int counter = 0)
    {
        counter+= Files.Count;
        foreach (var directory in Directories)
        {
            counter += directory.DescendantFileCount(counter);
        }
        return counter;
    }
}