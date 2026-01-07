using DigitalPreservation.Common.Model.Transit;
using Test.Helpers.TestData;

namespace Preservation.API.Tests.WorkingDirectories;

public class PathTests
{
    

    [Fact]
    public void Can_Find_Mets_File()
    {
        var root = TestStructure.GetTestMetsStructure();
        
        var mets = root.FindFile("mets.xml");
        
        mets!.LocalPath.Should().Be("mets.xml");
    }
    
    

    [Fact]
    public void Can_Find_Objects_Dir()
    {
        var root = TestStructure.GetTestMetsStructure();
        
        var objects = root.FindDirectory(FolderNames.Objects);
        
        objects!.LocalPath.Should().Be(FolderNames.Objects);
    }
    
    
    [Fact]
    public void Can_Find_Nested_File()
    {
        var root = TestStructure.GetTestMetsStructure();
        
        var image1 = root.FindFile("objects/image1.jpg");
        
        image1!.LocalPath.Should().Be("objects/image1.jpg");
    }
    
    
    
    [Fact]
    public void Can_Find_Child_File()
    {
        var root = TestStructure.GetTestMetsStructure();
        var objects = root.FindDirectory(FolderNames.Objects);
        
        var image1 = objects!.FindFile("image1.jpg");
        
        image1!.LocalPath.Should().Be("objects/image1.jpg");
    }
    
    
    [Fact]
    public void Can_Find_Relative_File()
    {
        var root = TestStructure.GetTestMetsStructure();
        var objects = root.FindDirectory(FolderNames.Objects);
        
        var subimage1 = objects!.FindFile("subdirectory/sub-image1.jpg");
        
        subimage1!.LocalPath.Should().Be("objects/subdirectory/sub-image1.jpg");
    }
    
}