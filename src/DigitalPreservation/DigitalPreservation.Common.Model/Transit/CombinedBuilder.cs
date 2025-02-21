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
        var directoryList = new List<CombinedDirectory>();
        if (metsWorkingDirectory is not null)
        {
            // assume that normally (eg for export) the METS has more than the file system
            foreach (var metsDirectory in metsWorkingDirectory.Directories)
            {
                if (fileSystemWorkingDirectory is not null)
                {
                    var fsDirectory = fileSystemWorkingDirectory.FindDirectory(metsDirectory.LocalPath);
                    // fsDirectory still might be null, but that's OK
                    directoryList.Add(new CombinedDirectory(fsDirectory, metsDirectory));
                }
                else
                {
                    directoryList.Add(new CombinedDirectory(null, metsDirectory));
                }
            }
        }

        if(fileSystemWorkingDirectory is not null)
        {
            foreach (var fsDirectory in fileSystemWorkingDirectory.Directories)
            {
                if (metsWorkingDirectory is not null)
                {
                    var metsDirectory = metsWorkingDirectory.FindDirectory(fsDirectory.LocalPath);
                    if (metsDirectory is null)
                    {
                        // directories in the file system that are NOT in METS; we would not have already added this
                        directoryList.Add(new CombinedDirectory(fsDirectory, metsDirectory));
                    }
                }
                else
                {
                    directoryList.Add(new CombinedDirectory(fsDirectory, null));
                }
            }
        }

        combined.Directories = directoryList.OrderBy(d => d.LocalPath!.GetSlug()).ToList();

        foreach (var combinedDirectory in combined.Directories)
        {
            // eg combinedDirectory is objects
            // we don't yet know about its child dirs
            // we need to build its child dirs
            var depositDirMap = new Dictionary<string, WorkingDirectory>();
            var metsDirMap = new Dictionary<string, WorkingDirectory>();
            if (combinedDirectory.DirectoryInDeposit is not null)
            {
                foreach (var fsDirectory in combinedDirectory.DirectoryInDeposit.Directories)
                {
                    depositDirMap.Add(fsDirectory.LocalPath, fsDirectory);
                }
            }
            if (combinedDirectory.DirectoryInMets is not null)
            {
                foreach (var metsDirectory in combinedDirectory.DirectoryInMets.Directories)
                {
                    metsDirMap.Add(metsDirectory.LocalPath, metsDirectory);
                }
            }
            var keys = depositDirMap.Keys.Union(metsDirMap.Keys);
            foreach (var key in keys.OrderBy(key => key.GetSlug()))
            {
                depositDirMap.TryGetValue(key, out var depositDirectory);
                metsDirMap.TryGetValue(key, out var metsDirectory);
                if (depositDirectory == null && metsDirectory == null)
                {
                    throw new Exception("Both entries are null");
                }
                combinedDirectory.Directories.Add(Build(depositDirectory, metsDirectory));
            }
        }
        
        // Binaries
        var fileList = new List<CombinedFile>();
        
        if (metsWorkingDirectory is not null)
        {
            foreach (var metsFile in metsWorkingDirectory.Files)
            {
                if (fileSystemWorkingDirectory is not null)
                {
                    var fsFile = fileSystemWorkingDirectory.FindFile(metsFile.GetSlug());
                    fileList.Add(new CombinedFile(fsFile, metsFile));
                }
                else
                {
                    fileList.Add(new CombinedFile(null, metsFile));
                }
            }
        }
        
        if(fileSystemWorkingDirectory is not null)
        {
            foreach (var fsFile in fileSystemWorkingDirectory.Files)
            {
                if (metsWorkingDirectory is not null)
                {
                    var metsFile = metsWorkingDirectory.FindFile(fsFile.GetSlug());
                    if (metsFile is null)
                    {
                        // directories in the file system that are NOT in METS; we would not have already added this
                        fileList.Add(new CombinedFile(fsFile, metsFile));
                    }
                }
                else
                {
                    fileList.Add(new CombinedFile(fsFile, null));
                }
            }
        }
        
        combined.Files = fileList.OrderBy(f => f.LocalPath!.GetSlug()).ToList();
        
        // Now recurse
        return combined;
    }
}