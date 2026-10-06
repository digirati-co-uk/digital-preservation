using DigitalPreservation.Common.Model.Import;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// RenameRefusalMessage is the shared wording the Preservation and Storage APIs both use to refuse
/// an import job that asks for a rename, which is not implemented yet (issue #260).
/// </summary>
public class ImportJobRenameRefusalTests
{
    [Fact]
    public void Returns_Null_When_Neither_List_Has_Renames()
    {
        new ImportJob().RenameRefusalMessage().Should().BeNull();
    }

    [Fact]
    public void Names_The_Count_And_Id_For_A_Single_Rename()
    {
        var job = new ImportJob();
        job.BinariesToRename.Add(new Binary { Id = new Uri("https://example.com/repo/thing/report.pdf") });

        var message = job.RenameRefusalMessage();

        message.Should().Contain("1 item(s)");
        message.Should().Contain("https://example.com/repo/thing/report.pdf");
    }

    [Fact]
    public void Counts_Across_Both_Lists()
    {
        var job = new ImportJob();
        job.BinariesToRename.Add(new Binary { Id = new Uri("https://example.com/repo/thing/report.pdf") });
        job.ContainersToRename.Add(new Container { Id = new Uri("https://example.com/repo/thing/folder") });

        job.RenameRefusalMessage().Should().Contain("2 item(s)");
    }

    [Fact]
    public void Lists_Only_The_First_Three_Ids_Then_An_Ellipsis()
    {
        var job = new ImportJob();
        for (var i = 0; i < 5; i++)
        {
            job.BinariesToRename.Add(new Binary { Id = new Uri($"https://example.com/repo/thing/file-{i}.pdf") });
        }

        var message = job.RenameRefusalMessage();

        message.Should().Contain("5 item(s)");
        message.Should().Contain("file-0.pdf").And.Contain("file-1.pdf").And.Contain("file-2.pdf");
        message.Should().NotContain("file-3.pdf");
        message.Should().Contain("…");
    }
}
