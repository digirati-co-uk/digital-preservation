using DigitalPreservation.Common.Model.Transit;

namespace Preservation.API.Tests.WorkingDirectories;

public class PathTests
{
    private WorkingDirectory GetTestStructure()
    {
        return new WorkingDirectory
        {
            LocalPath = string.Empty,
            Name = WorkingDirectory.DefaultRootName,
            Files =
            [
                new WorkingFile{LocalPath = "mets.xml", ContentType = "text/xml" }
            ],
            Directories =
            [
                new WorkingDirectory
                {
                    LocalPath = "objects",
                    Files = 
                    [
                        new WorkingFile{LocalPath = "objects/image1.jpg", ContentType = "image/jpg" },
                        new WorkingFile{LocalPath = "objects/image2.jpg", ContentType = "image/jpg" },
                        new WorkingFile{LocalPath = "objects/image3.jpg", ContentType = "image/jpg" }
                    ],
                    Directories = 
                    [
                        new WorkingDirectory
                        {
                            LocalPath = "objects/subdirectory",
                            Files = 
                            [
                                new WorkingFile{LocalPath = "objects/subdirectory/sub-image1.jpg", ContentType = "image/jpg" },
                                new WorkingFile{LocalPath = "objects/subdirectory/sub-image2.jpg", ContentType = "image/jpg" },
                                new WorkingFile{LocalPath = "objects/subdirectory/sub-image3.jpg", ContentType = "image/jpg" }
                            ]
                        }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void Can_Find_Mets_File()
    {
        var root = GetTestStructure();
        
        var mets = root.FindFile("mets.xml");
        
        mets!.LocalPath.Should().Be("mets.xml");
    }
    
    

    [Fact]
    public void Can_Find_Objects_Dir()
    {
        var root = GetTestStructure();
        
        var objects = root.FindDirectory("objects");
        
        objects!.LocalPath.Should().Be("objects");
    }
    
    
    [Fact]
    public void Can_Find_Nested_File()
    {
        var root = GetTestStructure();
        
        var image1 = root.FindFile("objects/image1.jpg");
        
        image1!.LocalPath.Should().Be("objects/image1.jpg");
    }
    
    
    
    [Fact]
    public void Can_Find_Child_File()
    {
        var root = GetTestStructure();
        var objects = root.FindDirectory("objects");
        
        var image1 = objects!.FindFile("image1.jpg");
        
        image1!.LocalPath.Should().Be("objects/image1.jpg");
    }
    
    
    [Fact]
    public void Can_Find_Relative_File()
    {
        var root = GetTestStructure();
        var objects = root.FindDirectory("objects");
        
        var subimage1 = objects!.FindFile("subdirectory/sub-image1.jpg");
        
        subimage1!.LocalPath.Should().Be("objects/subdirectory/sub-image1.jpg");
    }
    
}