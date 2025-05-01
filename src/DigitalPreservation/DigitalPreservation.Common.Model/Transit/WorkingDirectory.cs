using System.Text.Json.Serialization;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public class WorkingDirectory : WorkingBase
{
    public const string DefaultRootName = "__ROOT";
    
    [JsonPropertyOrder(0)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(WorkingDirectory); 
    
    [JsonPropertyName("files")]
    [JsonPropertyOrder(5)]
    public List<WorkingFile> Files { get; set; } = [];
    
    [JsonPropertyName("directories")]
    [JsonPropertyOrder(6)]
    public List<WorkingDirectory> Directories { get; set; } = [];

    public WorkingFile? FindFile(string path)
    {
        var parent = FindDirectory(path.GetParent());
        var slug = path.GetSlug();
        return parent?.Files.SingleOrDefault(f => f.LocalPath.GetSlug() == slug);
    }
    
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
    
    
    public Container ToContainer(Uri repositoryUri, Uri origin, List<string>? uris = null)
    {
        uris?.Add(repositoryUri.ToString());
        var container = new Container
        {
            Name = Name,
            Id = repositoryUri,
            Origin = origin
        };
        foreach (var wd in Directories)
        {
            var slug = wd.GetUriSafeSlug();
            container.Containers.Add(wd.ToContainer(repositoryUri.AppendSlug(slug), origin.AppendSlug(slug), uris));
        }
        foreach (var wf in Files)
        {
            var slug = wf.GetUriSafeSlug();
            var binaryId = repositoryUri.AppendSlug(slug);
            uris?.Add(binaryId.ToString());
            var fileFormatMetadata = wf.GetFileFormatMetadata();
            var size =fileFormatMetadata?.Size ?? wf.Size ?? 0;
            var contentType = fileFormatMetadata?.ContentType ?? wf.ContentType;
            var digest = wf.Digest.HasText() ? wf.Digest : fileFormatMetadata?.Digest;
            container.Binaries.Add(new Binary
            {
                Id = binaryId,
                Name = wf.Name ?? wf.GetSlug(), // not the Uri-Safe slug
                ContentType = contentType,
                Digest = digest,
                Size = size,
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

    public WorkingDirectory ToRootLayout()
    {
        if (!LocalPath.StartsWith($"{FolderNames.BagItData}/"))
        {
            return this;
        }

        return new WorkingDirectory
        {
            LocalPath = LocalPath.RemoveStart($"{FolderNames.BagItData}/")!,
            Directories = Directories.Select(d => d.ToRootLayout()).ToList(),
            Files = Files.Select(f => f.ToRootLayout()).ToList(),
            MetsExtensions = MetsExtensions,
            Modified = Modified,
            Name = Name,
            Metadata = Metadata
        };
    }
}