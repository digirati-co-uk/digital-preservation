using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
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

    public WorkingFile? FindFile(string path) // , bool useStorageLocation = false
    {
        var parent = FindDirectory(path.GetParent());
        var slug = path.GetSlug();
        // if (useStorageLocation)
        // {
        //     var file = parent?.Files.SingleOrDefault(f => f.GetStorageMetadata()?.StorageLocation?.GetSlug() == slug);
        //     if (file != null)
        //     {
        //         return file;
        //     }
        // }
        return parent?.Files.SingleOrDefault(f => f.LocalPath.GetSlug() == slug);
    }
    
    public WorkingDirectory? FindDirectory(string? path, bool create = false) // , bool useStorageLocation = false
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
            WorkingDirectory? potentialDirectory;
            // if (useStorageLocation)
            // {
            //     potentialDirectory = directory.Directories.SingleOrDefault(d => d.GetStorageMetadata()?.StorageLocation?.GetSlug() == part);
            // }
            // else
            // {
                potentialDirectory = directory.Directories.SingleOrDefault(d => d.GetSlug() == part);
            //}
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
    
    public static WorkingDirectory RootDirectory()
    {
        return new WorkingDirectory
        {
            LocalPath = string.Empty,
            Name = WorkingDirectory.DefaultRootName,
            Modified = DateTime.UtcNow
        };
    }
}