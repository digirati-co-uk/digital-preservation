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
            combinedDirectory.Directories.Add(Build(combinedDirectory.DirectoryInDeposit, combinedDirectory.DirectoryInMets));
        }
        
        // Binaries
        var fileList = new List<CombinedFile>();
        
        if (metsWorkingDirectory is not null)
        {
            foreach (var metsFile in metsWorkingDirectory.Files)
            {
                if (fileSystemWorkingDirectory is not null)
                {
                    var fsFile = fileSystemWorkingDirectory.FindFile(metsFile.LocalPath);
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
                    var metsFile = metsWorkingDirectory.FindFile(fsFile.LocalPath);
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