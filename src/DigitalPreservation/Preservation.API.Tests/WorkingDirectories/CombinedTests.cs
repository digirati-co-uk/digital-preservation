using DigitalPreservation.Common.Model.Transit;

namespace Preservation.API.Tests.WorkingDirectories;

public class CombinedTests
{
    [Fact]
    public void Combined_When_Both_Same()
    {
        var fileSystem = TestStructure.GetTestStructure();
        var mets = TestStructure.GetTestStructure();

        var combined = CombinedBuilder.Build(fileSystem, mets);

        combined.Directories.Should().HaveCount(1);
        combined.Directories[0].DirectoryInDeposit.Should().BeEquivalentTo(combined.Directories[0].DirectoryInMets);

        combined.Files.Should().HaveCount(1);
        combined.Files[0].LocalPath.Should().Be("mets.xml");

        combined.Directories[0].Files.Should().HaveCount(3);
        combined.Directories[0].Directories.Should().HaveCount(1);
        combined.Directories[0].Directories[0].Files.Should().HaveCount(3);
        combined.Directories[0].Directories[0].Files[1].LocalPath.Should().Be("objects/subdirectory/sub-image2.jpg");
    }

    [Fact]
    public void Combined_With_More_In_Mets()
    {
        var fileSystem = TestStructure.GetTestStructure();
        var mets = TestStructure.GetTestStructure();

        mets.Files.Add(new WorkingFile { LocalPath = "extra-file.txt", ContentType = "text/plain" });
        var extraDir = new WorkingDirectory { LocalPath = "extra-directory" };
        extraDir.Files.Add(new WorkingFile
            { LocalPath = "extra-directory/extra-file-1.txt", ContentType = "text/plain" });
        extraDir.Files.Add(new WorkingFile
            { LocalPath = "extra-directory/extra-file-2.txt", ContentType = "text/plain" });
        mets.Directories.Add(extraDir);

        var combined = CombinedBuilder.Build(fileSystem, mets);

        combined.Directories.Should().HaveCount(2);
        var objects = combined.Directories.Single(d => d.LocalPath == "objects");
        // alphabetical order
        (combined.Directories[1] == objects).Should().BeTrue();
        combined.Directories[1].DirectoryInDeposit.Should().BeEquivalentTo(combined.Directories[1].DirectoryInMets);
        combined.Directories[0].DirectoryInDeposit.Should().BeNull();
        combined.Directories[0].DirectoryInMets.Should().NotBeNull();

        combined.Files.Should().HaveCount(2);
        combined.Files[0].LocalPath.Should().Be("extra-file.txt");
        combined.Files[1].LocalPath.Should().Be("mets.xml");
        combined.Files[0].FileInDeposit.Should().BeNull();
        combined.Files[0].FileInMets.Should().NotBeNull();

        combined.Directories[1].Files.Should().HaveCount(3);
        combined.Directories[1].Directories.Should().HaveCount(1);
        combined.Directories[1].Directories[0].Files.Should().HaveCount(3);
        combined.Directories[1].Directories[0].Files[1].LocalPath.Should().Be("objects/subdirectory/sub-image2.jpg");
        combined.Directories[1].Directories[0].Files[1].FileInMets.Should().NotBeNull();
        combined.Directories[1].Directories[0].Files[1].FileInDeposit.Should().NotBeNull();

        combined.Directories[0].LocalPath.Should().Be("extra-directory");
        combined.Directories[0].DirectoryInMets.Should().NotBeNull();
        combined.Directories[0].DirectoryInDeposit.Should().BeNull();

        combined.Directories[0].Directories.Should().HaveCount(0);
        combined.Directories[0].Files.Should().HaveCount(2);
        combined.Directories[0].Files[0].LocalPath.Should().Be("extra-directory/extra-file-1.txt");
        combined.Directories[0].Files[1].LocalPath.Should().Be("extra-directory/extra-file-2.txt");

        combined.Directories[0].Files[1].FileInMets.Should().NotBeNull();
        combined.Directories[0].Files[1].FileInDeposit.Should().BeNull();
    }

    [Fact]
    public void Combined_With_More_In_Filesystem()
    {
        var fileSystem = TestStructure.GetTestStructure();
        var mets = TestStructure.GetTestStructure();

        fileSystem.Files.Add(new WorkingFile { LocalPath = "extra-file.txt", ContentType = "text/plain" });
        var extraDir = new WorkingDirectory { LocalPath = "extra-directory" };
        extraDir.Files.Add(new WorkingFile
            { LocalPath = "extra-directory/extra-file-1.txt", ContentType = "text/plain" });
        extraDir.Files.Add(new WorkingFile
            { LocalPath = "extra-directory/extra-file-2.txt", ContentType = "text/plain" });
        fileSystem.Directories.Add(extraDir);
        var combined = CombinedBuilder.Build(fileSystem, mets);

        combined.Directories.Should().HaveCount(2);
        var objects = combined.Directories.Single(d => d.LocalPath == "objects");
        // alphabetical order
        (combined.Directories[1] == objects).Should().BeTrue();
        combined.Directories[1].DirectoryInDeposit.Should().BeEquivalentTo(combined.Directories[1].DirectoryInMets);
        combined.Directories[0].DirectoryInDeposit.Should().NotBeNull();
        combined.Directories[0].DirectoryInMets.Should().BeNull();

        combined.Files.Should().HaveCount(2);
        combined.Files[0].LocalPath.Should().Be("extra-file.txt");
        combined.Files[1].LocalPath.Should().Be("mets.xml");
        combined.Files[0].FileInDeposit.Should().NotBeNull();
        combined.Files[0].FileInMets.Should().BeNull();

        combined.Directories[1].Files.Should().HaveCount(3);
        combined.Directories[1].Directories.Should().HaveCount(1);
        combined.Directories[1].Directories[0].Files.Should().HaveCount(3);
        combined.Directories[1].Directories[0].Files[1].LocalPath.Should().Be("objects/subdirectory/sub-image2.jpg");
        combined.Directories[1].Directories[0].Files[1].FileInMets.Should().NotBeNull();
        combined.Directories[1].Directories[0].Files[1].FileInDeposit.Should().NotBeNull();

        combined.Directories[0].LocalPath.Should().Be("extra-directory");
        combined.Directories[0].DirectoryInMets.Should().BeNull();
        combined.Directories[0].DirectoryInDeposit.Should().NotBeNull();

        combined.Directories[0].Directories.Should().HaveCount(0);
        combined.Directories[0].Files.Should().HaveCount(2);
        combined.Directories[0].Files[0].LocalPath.Should().Be("extra-directory/extra-file-1.txt");
        combined.Directories[0].Files[1].LocalPath.Should().Be("extra-directory/extra-file-2.txt");

        combined.Directories[0].Files[1].FileInMets.Should().BeNull();
        combined.Directories[0].Files[1].FileInDeposit.Should().NotBeNull();
    }

    [Fact]
    public void Combined_With_Multiple_Differences()
    {
        var fileSystem = TestStructure.GetTestStructure();
        var mets = TestStructure.GetTestStructure();

        fileSystem.Files.Add(new WorkingFile { LocalPath = "extra-fs-file.txt", ContentType = "text/plain" });
        var extraFsDir = new WorkingDirectory { LocalPath = "extra-fs-directory" };
        extraFsDir.Files.Add(new WorkingFile
            { LocalPath = "extra-fs-directory/extra-file-1.txt", ContentType = "text/plain" });
        extraFsDir.Files.Add(new WorkingFile
            { LocalPath = "extra-fs-directory/extra-file-2.txt", ContentType = "text/plain" });
        fileSystem.Directories.Add(extraFsDir);
        
        
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
        
        var combined = CombinedBuilder.Build(fileSystem, mets);
        combined.Files.Should().HaveCount(3);
        combined.Directories.Should().HaveCount(3);
        
        combined.Directories[0].LocalPath.Should().Be("extra-fs-directory");
        combined.Directories[1].LocalPath.Should().Be("extra-mets-directory");
        combined.Directories[2].LocalPath.Should().Be("objects");

        combined.Directories[1].Directories.Should().HaveCount(1);
        combined.Directories[1].Directories[0].LocalPath.Should().Be("extra-mets-directory/child-directory");
        combined.Directories[1].Directories[0].Files.Should().HaveCount(2);
        
        combined.Directories[1].Directories[0].Files[0].FileInMets.Should().NotBeNull();
        combined.Directories[1].Directories[0].Files[0].FileInDeposit.Should().BeNull();
        
        combined.Directories[0].Files[0].FileInDeposit.Should().NotBeNull();
        combined.Directories[0].Files[0].FileInMets.Should().BeNull();
    }
}