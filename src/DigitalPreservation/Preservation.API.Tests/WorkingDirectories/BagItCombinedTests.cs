using DigitalPreservation.Common.Model.Transit;
using Test.Helpers.TestData;

namespace Preservation.API.Tests.WorkingDirectories;

public class BagItCombinedTests
{
    [Fact]
    public void Combined_When_Both_Same()
    {
        var fileSystem = TestStructure.GetBagItTestStructure();
        var mets = TestStructure.GetTestMetsStructure();

        var offsetFileSystem = fileSystem.Directories.Single(d => d.LocalPath == FolderNames.BagItData);
        var combined = CombinedBuilder.BuildOffset(fileSystem, offsetFileSystem, mets);

        combined.Directories.Should().HaveCount(1);
        combined.Files.Should().HaveCount(1);
        var apparentRoot = combined.Directories[0];
        var otherWay = combined.Directories.Single(d => d.LocalPath == "");
        var anotherWay = combined.Directories.Single(d => d.DirectoryInDeposit!.LocalPath == FolderNames.BagItData);
        apparentRoot.Should().Be(otherWay);
        apparentRoot.Should().Be(anotherWay);
        anotherWay.Should().Be(otherWay);
        
        apparentRoot.LocalPath.Should().Be(""); // It's the apparent root
        apparentRoot.Whereabouts.Should().Be(Whereabouts.Both); // The data directory is in both - it's the root of METS
        apparentRoot.DirectoryInDeposit.Should().NotBeNull();
        apparentRoot.DirectoryInMets.Should().NotBeNull();  // The data directory is in both - it's the root of METS
        combined.Files[0].LocalPath.Should().Be("../bagit.txt");
        combined.Files[0].Whereabouts.Should().Be(Whereabouts.Extra);
        combined.Files[0].FileInDeposit.Should().NotBeNull();
        combined.Files[0].FileInMets.Should().BeNull(); // but this is not in METS

        apparentRoot.Files.Should().HaveCount(1);
        apparentRoot.Files[0].LocalPath.Should().Be("mets.xml");
        
        var objects = apparentRoot.Directories.Single(d => d.LocalPath == FolderNames.Objects);
        objects.LocalPath.Should().Be(FolderNames.Objects); 
        objects.Whereabouts.Should().Be(Whereabouts.Both);
        objects.DirectoryInDeposit.Should().NotBeNull();
        objects.DirectoryInMets.Should().NotBeNull();
        
        objects.Files.Should().HaveCount(3);
        objects.Directories.Should().HaveCount(1);
        objects.Directories[0].Files.Should().HaveCount(3);
        objects.Directories[0].Files[1].LocalPath.Should().Be("objects/subdirectory/sub-image2.jpg");
        
        var metadata = apparentRoot.Directories.Single(d => d.LocalPath == FolderNames.Metadata);
        metadata.LocalPath.Should().Be(FolderNames.Metadata); 
        metadata.Whereabouts.Should().Be(Whereabouts.Deposit);
        metadata.DirectoryInDeposit.Should().NotBeNull();
        metadata.DirectoryInMets.Should().BeNull();
    }

    [Fact]
    public void Combined_With_More_In_Mets()
    {
        var fileSystem = TestStructure.GetBagItTestStructure();
        var mets = TestStructure.GetTestMetsStructure();

        mets.Files.Add(new WorkingFile { LocalPath = "extra-file.txt", ContentType = "text/plain" });
        var extraDir = new WorkingDirectory { LocalPath = "extra-directory" };
        extraDir.Files.Add(new WorkingFile
            { LocalPath = "extra-directory/extra-file-1.txt", ContentType = "text/plain" });
        extraDir.Files.Add(new WorkingFile
            { LocalPath = "extra-directory/extra-file-2.txt", ContentType = "text/plain" });
        mets.Directories.Add(extraDir);

        var offsetFileSystem = fileSystem.Directories.Single(d => d.LocalPath == FolderNames.BagItData);
        var combined = CombinedBuilder.BuildOffset(fileSystem, offsetFileSystem, mets);

        var apparentRoot = combined.Directories.Single(d => d.LocalPath == "");
        apparentRoot.Directories.Should().HaveCount(3); // because /metadata too
        var objects = apparentRoot.Directories.Single(d => d.LocalPath == FolderNames.Objects);
        // alphabetical order
        (apparentRoot.Directories[2] == objects).Should().BeTrue();
        apparentRoot.Directories[2].DirectoryInDeposit.Should().NotBeEquivalentTo(apparentRoot.Directories[1].DirectoryInMets);
        apparentRoot.Directories[0].DirectoryInDeposit.Should().BeNull();
        apparentRoot.Directories[0].DirectoryInMets.Should().NotBeNull();

        apparentRoot.Files.Should().HaveCount(2);
        apparentRoot.Files[0].LocalPath.Should().Be("extra-file.txt");
        apparentRoot.Files[1].LocalPath.Should().Be("mets.xml");
        apparentRoot.Files[0].FileInDeposit.Should().BeNull();
        apparentRoot.Files[0].FileInMets.Should().NotBeNull();

        apparentRoot.Directories[2].Files.Should().HaveCount(3);
        apparentRoot.Directories[2].Directories.Should().HaveCount(1);
        apparentRoot.Directories[2].Directories[0].Files.Should().HaveCount(3);
        apparentRoot.Directories[2].Directories[0].Files[1].LocalPath.Should().Be("objects/subdirectory/sub-image2.jpg");
        apparentRoot.Directories[2].Directories[0].Files[1].FileInMets.Should().NotBeNull();
        apparentRoot.Directories[2].Directories[0].Files[1].FileInDeposit.Should().NotBeNull();

        apparentRoot.Directories[0].LocalPath.Should().Be("extra-directory");
        apparentRoot.Directories[0].DirectoryInMets.Should().NotBeNull();
        apparentRoot.Directories[0].DirectoryInDeposit.Should().BeNull();

        apparentRoot.Directories[0].Directories.Should().HaveCount(0);
        apparentRoot.Directories[0].Files.Should().HaveCount(2);
        apparentRoot.Directories[0].Files[0].LocalPath.Should().Be("extra-directory/extra-file-1.txt");
        apparentRoot.Directories[0].Files[1].LocalPath.Should().Be("extra-directory/extra-file-2.txt");

        apparentRoot.Directories[0].Files[1].FileInMets.Should().NotBeNull();
        apparentRoot.Directories[0].Files[1].FileInDeposit.Should().BeNull();
    }

    [Fact]
    public void Combined_With_More_In_Filesystem()
    {
        var fileSystem = TestStructure.GetBagItTestStructure();
        var mets = TestStructure.GetTestMetsStructure();
        var offsetFileSystem = fileSystem.Directories.Single(d => d.LocalPath == FolderNames.BagItData);
        offsetFileSystem.Files.Add(new WorkingFile { LocalPath = "data/extra-file.txt", ContentType = "text/plain" });
        var extraDir = new WorkingDirectory { LocalPath = "data/extra-directory" };
        extraDir.Files.Add(new WorkingFile
            { LocalPath = "data/extra-directory/extra-file-1.txt", ContentType = "text/plain" });
        extraDir.Files.Add(new WorkingFile
            { LocalPath = "data/extra-directory/extra-file-2.txt", ContentType = "text/plain" });
        offsetFileSystem.Directories.Add(extraDir);
        
        var combined = CombinedBuilder.BuildOffset(fileSystem, offsetFileSystem, mets);
        combined.Directories.Should().HaveCount(1); // still data
        var apparentRoot = combined.Directories.Single(d => d.LocalPath == "");
        apparentRoot.Directories.Should().HaveCount(3); // extra-directory, metadata, objects
        var objects = apparentRoot.Directories.Single(d => d.LocalPath == FolderNames.Objects);
        // alphabetical order extras, metadata, objects
        (apparentRoot.Directories[2] == objects).Should().BeTrue();
        apparentRoot.Directories[2].DirectoryInDeposit.Should().NotBeEquivalentTo(apparentRoot.Directories[2].DirectoryInMets);
        apparentRoot.Directories[0].DirectoryInDeposit.Should().NotBeNull();
        apparentRoot.Directories[0].DirectoryInMets.Should().BeNull(); // extra

        apparentRoot.Files.Should().HaveCount(2);
        apparentRoot.Files[0].LocalPath.Should().Be("extra-file.txt");
        apparentRoot.Files[1].LocalPath.Should().Be("mets.xml");
        apparentRoot.Files[0].FileInDeposit.Should().NotBeNull();
        apparentRoot.Files[0].FileInMets.Should().BeNull();

        apparentRoot.Directories[2].Files.Should().HaveCount(3);
        apparentRoot.Directories[2].Directories.Should().HaveCount(1);
        apparentRoot.Directories[2].Directories[0].Files.Should().HaveCount(3);
        apparentRoot.Directories[2].Directories[0].Files[1].LocalPath.Should().Be("objects/subdirectory/sub-image2.jpg");
        apparentRoot.Directories[2].Directories[0].Files[1].FileInMets.Should().NotBeNull();
        apparentRoot.Directories[2].Directories[0].Files[1].FileInDeposit.Should().NotBeNull();

        apparentRoot.Directories[0].LocalPath.Should().Be("extra-directory");
        apparentRoot.Directories[0].DirectoryInMets.Should().BeNull();
        apparentRoot.Directories[0].DirectoryInDeposit.Should().NotBeNull();

        apparentRoot.Directories[0].Directories.Should().HaveCount(0);
        apparentRoot.Directories[0].Files.Should().HaveCount(2);
        apparentRoot.Directories[0].Files[0].LocalPath.Should().Be("extra-directory/extra-file-1.txt");
        apparentRoot.Directories[0].Files[1].LocalPath.Should().Be("extra-directory/extra-file-2.txt");

        apparentRoot.Directories[0].Files[1].FileInMets.Should().BeNull();
        apparentRoot.Directories[0].Files[1].FileInDeposit.Should().NotBeNull();
    }

    [Fact]
    public void Combined_With_Multiple_Differences()
    {
        var fileSystem = TestStructure.GetBagItTestStructure();
        var mets = TestStructure.GetTestMetsStructure();
        var offsetFileSystem = fileSystem.Directories.Single(d => d.LocalPath == FolderNames.BagItData);

        offsetFileSystem.Files.Add(new WorkingFile { LocalPath = "data/extra-fs-file.txt", ContentType = "text/plain" });
        var extraFsDir = new WorkingDirectory { LocalPath = "data/extra-fs-directory" };
        extraFsDir.Files.Add(new WorkingFile
            { LocalPath = "data/extra-fs-directory/extra-file-1.txt", ContentType = "text/plain" });
        extraFsDir.Files.Add(new WorkingFile
            { LocalPath = "data/extra-fs-directory/extra-file-2.txt", ContentType = "text/plain" });
        offsetFileSystem.Directories.Add(extraFsDir);
        
        
        mets.Files.Add(new WorkingFile { LocalPath = "extra-mets-file.txt", ContentType = "text/plain" });
        var extraMetsDir = new WorkingDirectory { LocalPath = "extra-mets-directory" };
        extraMetsDir.Files.Add(new WorkingFile
            { LocalPath = "extra-mets-directory/extra-file-1.txt", ContentType = "text/plain" });
        extraMetsDir.Files.Add(new WorkingFile
            { LocalPath = "extra-mets-directory/extra-file-2.txt", ContentType = "text/plain" });
        mets.Directories.Add(extraMetsDir);
        
        var extraMetsDir2 = new WorkingDirectory { LocalPath = "extra-mets-directory/child-directory" };
        extraMetsDir2.Files.Add(new WorkingFile
            { LocalPath = "extra-mets-directory/child-directory/file-1.txt", ContentType = "text/plain" });
        extraMetsDir2.Files.Add(new WorkingFile
            { LocalPath = "extra-mets-directory/child-directory/file-2.txt", ContentType = "text/plain" });
        extraMetsDir.Directories.Add(extraMetsDir2);
        
        var combined = CombinedBuilder.BuildOffset(fileSystem, offsetFileSystem, mets);
        combined.Directories.Should().HaveCount(1); // still data
        var apparentRoot = combined.Directories.Single(d => d.LocalPath == "");
        apparentRoot.Directories.Should().HaveCount(4); // extra-fs-directory, extra-mets-directory, metadata, objects
        apparentRoot.Files.Should().HaveCount(3);
        
        apparentRoot.Directories[0].LocalPath.Should().Be("extra-fs-directory");
        apparentRoot.Directories[1].LocalPath.Should().Be("extra-mets-directory");
        apparentRoot.Directories[2].LocalPath.Should().Be(FolderNames.Metadata);
        apparentRoot.Directories[3].LocalPath.Should().Be(FolderNames.Objects);

        apparentRoot.Directories[1].Directories.Should().HaveCount(1);
        apparentRoot.Directories[1].Directories[0].LocalPath.Should().Be("extra-mets-directory/child-directory");
        apparentRoot.Directories[1].Directories[0].Files.Should().HaveCount(2);
        
        apparentRoot.Directories[1].Directories[0].Files[0].FileInMets.Should().NotBeNull();
        apparentRoot.Directories[1].Directories[0].Files[0].FileInDeposit.Should().BeNull();
        
        apparentRoot.Directories[0].Files[0].FileInDeposit.Should().NotBeNull();
        apparentRoot.Directories[0].Files[0].FileInMets.Should().BeNull();
    }

    [Fact]
    public void Use_Real_Working_Filesystem()
    {
        
        var fileSystem = TestStructure.GetBagItFileSystemStructure();
        var mets = TestStructure.GetActualMetsTemplate();
        var offsetFileSystem = fileSystem.Directories.Single(d => d.LocalPath == FolderNames.BagItData);
        var combined = CombinedBuilder.BuildOffset(fileSystem, offsetFileSystem, mets);
        var apparentRoot = combined.Directories.Single(d => d.LocalPath == "");
        apparentRoot.Files.Should().HaveCount(1);
        apparentRoot.Files[0].LocalPath.Should().Be("mets.xml");
    }
}