using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public static class CombinedBuilder
{
    
    public static CombinedDirectory BuildOffset(WorkingDirectory fileSystemRoot, WorkingDirectory offsetFileSystemDirectory, WorkingDirectory? metsWrapperPhysicalStructure)
    {
        var relativePath = offsetFileSystemDirectory.LocalPath;
        // we can't "join the streams" until we have built the top layer - but that should ONLY be the data dir and root files
        var combinedRoot = new CombinedDirectory(fileSystemRoot, null, relativePath);
        var offsetRoot = Build(offsetFileSystemDirectory, metsWrapperPhysicalStructure, relativePath);
        combinedRoot.Directories.Add(offsetRoot);
        foreach (var rootFile in fileSystemRoot.Files)
        {
            combinedRoot.Files.Add(new CombinedFile(rootFile, null, relativePath));
        }

        return combinedRoot;
    }
    
    public static CombinedDirectory Build(
        WorkingDirectory? fileSystemWorkingDirectory,
        WorkingDirectory? metsWorkingDirectory,
        string? relativePath = null)
    {
        var combined = new CombinedDirectory(fileSystemWorkingDirectory, metsWorkingDirectory, relativePath);

        // Directories
        var depositDirMap = new Dictionary<string, WorkingDirectory>();
        var metsDirMap = new Dictionary<string, WorkingDirectory>();
        if (fileSystemWorkingDirectory is not null)
        {
            foreach (var fsDirectory in fileSystemWorkingDirectory.Directories)
            {
                if (relativePath.HasText())
                {
                    depositDirMap.Add(fsDirectory.LocalPath.RemoveStart($"{relativePath}/")!, fsDirectory);
                }
                else
                {
                    depositDirMap.Add(fsDirectory.LocalPath, fsDirectory);
                }
            }
        }
        if (metsWorkingDirectory is not null)
        {
            foreach (var metsDirectory in metsWorkingDirectory.Directories)
            {
                metsDirMap.Add(metsDirectory.LocalPath, metsDirectory);
            }
        }
        var dirPaths = depositDirMap.Keys.Union(metsDirMap.Keys);
        foreach (var path in dirPaths.OrderBy(p => p.GetSlug()))
        {
            depositDirMap.TryGetValue(path, out var depositDirectory);
            metsDirMap.TryGetValue(path, out var metsDirectory);
            if (depositDirectory == null && metsDirectory == null)
            {
                throw new Exception("Both entries are null");
            }
            combined.Directories.Add(Build(depositDirectory, metsDirectory, relativePath));
        }
        

        
        // Binaries
        var depositFileMap = new Dictionary<string, WorkingFile>();
        var metsFileMap = new Dictionary<string, WorkingFile>();
        if (fileSystemWorkingDirectory is not null)
        {
            foreach (var fsFile in fileSystemWorkingDirectory.Files)
            {
                if (relativePath.HasText())
                {
                    depositFileMap.Add(fsFile.LocalPath.RemoveStart($"{relativePath}/")!, fsFile);
                }
                else
                {
                    depositFileMap.Add(fsFile.LocalPath, fsFile);
                }
            }
        }
        if (metsWorkingDirectory is not null)
        {
            foreach (var metsFile in metsWorkingDirectory.Files)
            {
                metsFileMap.Add(metsFile.LocalPath, metsFile);
            }
        }
        var filePaths = depositFileMap.Keys.Union(metsFileMap.Keys);
        foreach (var path in filePaths.OrderBy(p => p.GetSlug()))
        {
            depositFileMap.TryGetValue(path, out var depositFile);
            metsFileMap.TryGetValue(path, out var metsFile);
            if (depositFile == null && metsFile == null)
            {
                throw new Exception("Both entries are null");
            }
            combined.Files.Add(new CombinedFile(depositFile, metsFile, relativePath));
        }
        
        
        // Now recurse
        return combined;
    }

}