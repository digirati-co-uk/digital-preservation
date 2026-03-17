using DigitalPreservation.Common.Model.Transit;
using Test.Helpers.TestData;

namespace Preservation.API.Tests.WorkingDirectories;

public class ToRootLayoutTests
{
    // ToRootLayout() strips the "data/" prefix from a WorkingDirectory and all its
    // descendants, transforming a BagIt-layout subtree into a root-layout subtree.
    // It is called on individual directories/files retrieved from a BagIt workspace
    // (e.g. data/objects, data/metadata) before handing them to METS processing.
    //
    // The BagIt structure used in these tests (from TestStructure.GetBagItTestStructure()):
    //
    //   (root, LocalPath="")
    //   ├── bagit.txt
    //   └── data/                              (LocalPath="data")
    //       ├── data/objects/                  (LocalPath="data/objects")
    //       │   ├── data/objects/image1.jpg
    //       │   ├── data/objects/image2.jpg
    //       │   ├── data/objects/image3.jpg
    //       │   └── data/objects/subdirectory/ (LocalPath="data/objects/subdirectory")
    //       │       ├── data/objects/subdirectory/sub-image1.jpg
    //       │       ├── data/objects/subdirectory/sub-image2.jpg
    //       │       └── data/objects/subdirectory/sub-image3.jpg
    //       └── data/metadata/                 (LocalPath="data/metadata")
    //           └── data/metadata/tool-output.yaml

    [Fact]
    public void Can_Convert_BagIt_Root()
    {
        var data = TestStructure.GetBagItTestStructure().FindDirectory("data")!;

        var result = data.ToRootLayout();

        result.LocalPath.Should().Be("");
        result.FindDirectory("objects")!.LocalPath.Should().Be("objects");
    }
    
    [Fact]
    public void Objects_directory_path_has_prefix_stripped()
    {
        var objects = TestStructure.GetBagItTestStructure().FindDirectory("data/objects")!;

        var result = objects.ToRootLayout();

        result.LocalPath.Should().Be("objects");
    }

    [Fact]
    public void Nested_subdirectory_path_has_prefix_stripped()
    {
        var objects = TestStructure.GetBagItTestStructure().FindDirectory("data/objects")!;

        var result = objects.ToRootLayout();

        result.FindDirectory("subdirectory")!.LocalPath.Should().Be("objects/subdirectory");
    }

    [Fact]
    public void Files_in_objects_directory_have_prefix_stripped()
    {
        var objects = TestStructure.GetBagItTestStructure().FindDirectory("data/objects")!;

        var result = objects.ToRootLayout();

        result.Files.Select(f => f.LocalPath).Should().BeEquivalentTo(
        [
            "objects/image1.jpg",
            "objects/image2.jpg",
            "objects/image3.jpg"
        ]);
    }

    [Fact]
    public void Files_in_nested_subdirectory_have_prefix_stripped()
    {
        var objects = TestStructure.GetBagItTestStructure().FindDirectory("data/objects")!;

        var result = objects.ToRootLayout();

        var subdir = result.FindDirectory("subdirectory")!;
        subdir.Files.Select(f => f.LocalPath).Should().BeEquivalentTo(
        [
            "objects/subdirectory/sub-image1.jpg",
            "objects/subdirectory/sub-image2.jpg",
            "objects/subdirectory/sub-image3.jpg"
        ]);
    }

    [Fact]
    public void Metadata_directory_path_has_prefix_stripped()
    {
        var metadata = TestStructure.GetBagItTestStructure().FindDirectory("data/metadata")!;

        var result = metadata.ToRootLayout();

        result.LocalPath.Should().Be("metadata");
    }

    [Fact]
    public void File_in_metadata_directory_has_prefix_stripped()
    {
        var metadata = TestStructure.GetBagItTestStructure().FindDirectory("data/metadata")!;

        var result = metadata.ToRootLayout();

        result.Files.Should().ContainSingle()
            .Which.LocalPath.Should().Be("metadata/tool-output.yaml");
    }

    [Fact]
    public void File_count_is_preserved_after_transformation()
    {
        var objects = TestStructure.GetBagItTestStructure().FindDirectory("data/objects")!;

        var result = objects.ToRootLayout();

        result.DescendantFileCount().Should().Be(objects.DescendantFileCount());
    }

    [Fact]
    public void Directory_without_data_prefix_is_returned_unchanged()
    {
        var regular = new WorkingDirectory
        {
            LocalPath = "objects",
            Files = [new WorkingFile { LocalPath = "objects/image.jpg" }],
            Directories = []
        };

        var result = regular.ToRootLayout();

        result.Should().BeSameAs(regular);
    }
}
