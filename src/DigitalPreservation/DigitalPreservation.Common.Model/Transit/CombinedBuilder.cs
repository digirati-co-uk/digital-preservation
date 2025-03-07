using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit;

public static class CombinedBuilder
{
    public static CombinedDirectory Build(
        WorkingDirectory? fileSystemWorkingDirectory,
        WorkingDirectory? metsWorkingDirectory)
    {
        var combined = new CombinedDirectory(fileSystemWorkingDirectory, metsWorkingDirectory);

        // Directories
        var depositDirMap = new Dictionary<string, WorkingDirectory>();
        var metsDirMap = new Dictionary<string, WorkingDirectory>();
        if (fileSystemWorkingDirectory is not null)
        {
            foreach (var fsDirectory in fileSystemWorkingDirectory.Directories)
            {
                depositDirMap.Add(fsDirectory.LocalPath, fsDirectory);
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
            combined.Directories.Add(Build(depositDirectory, metsDirectory));
        }
        

        
        // Binaries
        var depositFileMap = new Dictionary<string, WorkingFile>();
        var metsFileMap = new Dictionary<string, WorkingFile>();
        if (fileSystemWorkingDirectory is not null)
        {
            foreach (var fsFile in fileSystemWorkingDirectory.Files)
            {
                depositFileMap.Add(fsFile.LocalPath, fsFile);
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
            combined.Files.Add(new CombinedFile(depositFile, metsFile));
        }
        
        
        // Now recurse
        return combined;
    }
}