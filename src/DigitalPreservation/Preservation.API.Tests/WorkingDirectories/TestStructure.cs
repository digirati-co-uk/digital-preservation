using DigitalPreservation.Common.Model.Transit;

namespace Preservation.API.Tests.WorkingDirectories;

public class TestStructure
{
    public static WorkingDirectory GetTestStructure()
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
}