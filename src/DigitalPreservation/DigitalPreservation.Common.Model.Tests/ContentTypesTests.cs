using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
namespace DigitalPreservation.Common.Model.Tests;

/// <summary>
/// The browser (via S3) and Siegfried (via the pipeline) each give a content type for a deposit file,
/// and GetBestContentType has to pick one. An .m4a upload arrives as audio/x-m4a and Siegfried says
/// audio/mp4: the same format under two names, which no rule resolved, so the deposit showed a
/// METS/deposit mismatch with an empty "Best".
/// </summary>
public class ContentTypesTests
{
    [Theory]
    [InlineData("audio/x-m4a", "audio/mp4")]
    [InlineData("audio/m4a", "audio/mp4")]
    [InlineData("AUDIO/X-M4A", "audio/mp4")]
    [InlineData("audio/x-wav", "audio/wav")]
    [InlineData("audio/mp3", "audio/mpeg")]
    [InlineData("image/jpg", "image/jpeg")]
    [InlineData("application/x-pdf", "application/pdf")]
    [InlineData("audio/mp4", "audio/mp4")]
    [InlineData("image/tiff", "image/tiff")]
    public void Normalise_Maps_Synonyms_To_The_Preferred_Name(string contentType, string expected)
    {
        ContentTypes.Normalise(contentType).Should().Be(expected);
    }

    [Fact]
    public void Normalise_Leaves_Null_As_Null()
    {
        ContentTypes.Normalise(null).Should().BeNull();
    }

    [Fact]
    public void Browser_And_Siegfried_Synonyms_Agree_On_The_Preferred_Name()
    {
        var file = DepositFile("audio/x-m4a", "audio/mp4");

        ContentTypes.GetBestContentType(file).Should().Be("audio/mp4");
        ContentTypes.GetAllContentTypes(file).Should().Equal("audio/mp4");
    }

    [Fact]
    public void A_Browser_Synonym_Alone_Is_Normalised()
    {
        // Before the pipeline has run, this is what gets written to the METS
        var file = DepositFile("audio/x-m4a", siegfriedContentType: null);

        ContentTypes.GetBestContentType(file).Should().Be("audio/mp4");
    }

    [Theory]
    [InlineData("application/octet-stream", "image/tiff", "image/tiff")]
    [InlineData("application/msword", "application/rtf", "application/rtf")]
    [InlineData("text/xml", "application/octet-stream", "text/xml")]
    [InlineData("application/zip", "audio/wav", "audio/wav")]
    public void Existing_Rules_Still_Pick_A_Winner(string browserContentType, string siegfriedContentType, string expected)
    {
        var file = DepositFile(browserContentType, siegfriedContentType);

        ContentTypes.GetBestContentType(file).Should().Be(expected);
    }

    [Fact]
    public void Genuinely_Different_Types_Have_No_Winner()
    {
        var file = DepositFile("audio/mp4", "audio/mpeg");

        ContentTypes.GetBestContentType(file).Should().BeNull();
    }

    [Fact]
    public void No_Mismatch_When_METS_Holds_The_Browser_Synonym()
    {
        // The case from the deposit page: METS written at upload says audio/x-m4a, the pipeline
        // then adds Siegfried's audio/mp4 to the deposit file.
        var combined = new CombinedFile(
            DepositFile("audio/x-m4a", "audio/mp4"),
            MetsFile("audio/x-m4a"));

        combined.MisMatches.Should().BeEmpty();
    }

    [Fact]
    public void Import_Uses_The_Preferred_Name_When_METS_Holds_The_Browser_Synonym()
    {
        // METS from before this change (or preserved METS) still says audio/x-m4a; that must not
        // turn into "different content types in METS and Deposit" and fail the import job.
        var combined = new CombinedFile(
            DepositFile("audio/x-m4a", "audio/mp4"),
            MetsFile("audio/x-m4a"));

        combined.GetSingleContentTypeFromMetsAndDeposit().Should().Be("audio/mp4");
    }

    [Fact]
    public void Mismatch_Still_Reported_For_Genuinely_Different_Types()
    {
        var combined = new CombinedFile(
            DepositFile("audio/mpeg", "audio/mpeg"),
            MetsFile("audio/mp4"));

        combined.MisMatches.Should().ContainSingle(m => m.Field == "ContentType");
        combined.GetSingleContentTypeFromMetsAndDeposit().Should().BeNull();
    }

    private const string LocalPath = "objects/interview.m4a";
    private const string Digest = "e1c06a64";

    private static WorkingFile DepositFile(string browserContentType, string? siegfriedContentType)
    {
        var file = new WorkingFile
        {
            LocalPath = LocalPath,
            ContentType = browserContentType,
            Digest = Digest
        };
        if (siegfriedContentType != null)
        {
            file.Metadata.Add(FileFormat("Siegfried", siegfriedContentType));
        }
        return file;
    }

    private static WorkingFile MetsFile(string contentType)
    {
        return new WorkingFile
        {
            LocalPath = LocalPath,
            ContentType = contentType,
            Digest = Digest,
            Metadata = [FileFormat("METS", contentType)]
        };
    }

    private static FileFormatMetadata FileFormat(string source, string contentType)
    {
        return new FileFormatMetadata
        {
            Source = source,
            ContentType = contentType,
            PronomKey = "fmt/2094",
            FormatName = "MPEG-4 Audio",
            Digest = Digest,
            Size = 1000
        };
    }
}
