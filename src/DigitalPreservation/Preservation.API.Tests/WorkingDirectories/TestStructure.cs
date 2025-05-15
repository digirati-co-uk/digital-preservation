using System.Text.Json;
using DigitalPreservation.Common.Model.Transit;

namespace Preservation.API.Tests.WorkingDirectories;

public class TestStructure
{
    public static WorkingDirectory GetBagItTestStructure()
    {
        return new WorkingDirectory
        {
            LocalPath = string.Empty,
            Name = WorkingDirectory.DefaultRootName,
            Files =
            [
                new WorkingFile{LocalPath = "bagit.txt", ContentType = "text/plain" }
            ],
            Directories =
            [
                new WorkingDirectory
                {
                    LocalPath = FolderNames.BagItData,
                    Files = [],
                    Directories = 
                    [
                        new WorkingDirectory
                        {
                            LocalPath = "data/objects",
                            Files = 
                            [
                                new WorkingFile{LocalPath = "data/objects/image1.jpg", ContentType = "image/jpg" },
                                new WorkingFile{LocalPath = "data/objects/image2.jpg", ContentType = "image/jpg" },
                                new WorkingFile{LocalPath = "data/objects/image3.jpg", ContentType = "image/jpg" }
                            ],
                            Directories = 
                            [
                                new WorkingDirectory
                                {
                                    LocalPath = "data/objects/subdirectory",
                                    Files = 
                                    [
                                        new WorkingFile{LocalPath = "data/objects/subdirectory/sub-image1.jpg", ContentType = "image/jpg" },
                                        new WorkingFile{LocalPath = "data/objects/subdirectory/sub-image2.jpg", ContentType = "image/jpg" },
                                        new WorkingFile{LocalPath = "data/objects/subdirectory/sub-image3.jpg", ContentType = "image/jpg" }
                                    ]
                                }
                            ]
                        },
                        new WorkingDirectory
                        {
                            LocalPath = "data/metadata",
                            Files = [
                                new WorkingFile{LocalPath = "data/metadata/tool-output.yaml", ContentType = "text/yaml" }
                            ]
                        }
                    ]
                }
            ]
        };
    }
    public static WorkingDirectory GetTestMetsStructure()
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
                    LocalPath = FolderNames.Objects,
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

    public static WorkingDirectory GetBagItFileSystemStructure()
    {
        var json = """
                   {
                     "type": "WorkingDirectory",
                     "localPath": "",
                     "name": "__ROOT",
                     "modified": "2025-04-23T16:50:48.0982194Z",
                     "files": [
                       {
                         "type": "WorkingFile",
                         "localPath": "__METSlike.json",
                         "name": "__METSlike.json",
                         "modified": "2025-04-23T16:50:47Z",
                         "contentType": "application/json",
                         "digest": "55db7dbc746857b1fde06f1df4db3d43ac02233f40d4a010c7b0e6756dfb7908",
                         "size": 724
                       }
                     ],
                     "directories": [
                       {
                         "type": "WorkingDirectory",
                         "localPath": "data",
                         "name": "data",
                         "modified": "2025-04-23T16:50:47Z",
                         "files": [
                           {
                             "type": "WorkingFile",
                             "localPath": "data/mets.xml",
                             "name": null,
                             "modified": "2025-04-23T16:50:48Z",
                             "contentType": "application/xml",
                             "digest": "b04009c745c94ebf1db79881c241836bcee6cc7f793f4ca4ec0cc8b8c8427d6c",
                             "size": 2031
                           }
                         ],
                         "directories": [
                           {
                             "type": "WorkingDirectory",
                             "localPath": "data/metadata",
                             "name": "metadata",
                             "modified": "2025-04-23T16:50:47Z",
                             "files": [],
                             "directories": []
                           },
                           {
                             "type": "WorkingDirectory",
                             "localPath": "data/objects",
                             "name": "objects",
                             "modified": "2025-04-23T16:50:47Z",
                             "files": [],
                             "directories": []
                           }
                         ]
                       }
                     ]
                   }
                   """;
        var wd = JsonSerializer.Deserialize<WorkingDirectory>(json);
        return wd!;
    }

    public static WorkingDirectory GetActualMetsTemplate()
    {
        const string json = """
                            {
                              "type": "WorkingDirectory",
                              "localPath": "",
                              "name": "__ROOT",
                              "modified": "2025-04-24T08:25:09.8741297Z",
                              "files": [
                                {
                                  "type": "WorkingFile",
                                  "localPath": "mets.xml",
                                  "name": "mets.xml",
                                  "modified": "0001-01-01T00:00:00",
                                  "contentType": "application/xml",
                                  "digest": "b04009c745c94ebf1db79881c241836bcee6cc7f793f4ca4ec0cc8b8c8427d6c",
                                  "size": null
                                }
                              ],
                              "directories": [
                                {
                                  "type": "WorkingDirectory",
                                  "localPath": "metadata",
                                  "name": "metadata",
                                  "modified": "0001-01-01T00:00:00",
                                  "files": [],
                                  "directories": [],
                                  "metsExtensions": {
                                    "physDivId": "PHYS_metadata",
                                    "admId": "ADM_metadata",
                                    "accessCondition": "Open",
                                    "originalPath": "metadata"
                                  }
                                },
                                {
                                  "type": "WorkingDirectory",
                                  "localPath": "objects",
                                  "name": "objects",
                                  "modified": "0001-01-01T00:00:00",
                                  "files": [],
                                  "directories": [],
                                  "metsExtensions": {
                                    "physDivId": "PHYS_objects",
                                    "admId": "ADM_objects",
                                    "accessCondition": "Open",
                                    "originalPath": "objects"
                                  }
                                }
                              ]
                            }
                            
                            """;
        
        var wd = JsonSerializer.Deserialize<WorkingDirectory>(json);
        return wd!;
    }
}