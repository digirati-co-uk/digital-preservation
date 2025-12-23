using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

namespace DigitalPreservation.Core.Tests.Lists;
public class ListsComparer
{
    [Fact]
    public void CompareTwoTagValueListsWithMismatchesInBoth()
    {
        var depositExifMetadata = new List<ExifTag>();
        depositExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:42" });
        depositExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:44" });
        depositExifMetadata.Add(new ExifTag { TagName = "ColorVersion", TagValue = "Blah" });
        depositExifMetadata.Add(new ExifTag { TagName = "Duration", TagValue = "Blah" });

        var metsExifMetadata = new List<ExifTag>();
        metsExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:43" });
        metsExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:45" });
        metsExifMetadata.Add(new ExifTag { TagName = "ColorVersion", TagValue = "Blah" });
        metsExifMetadata.Add(new ExifTag { TagName = "MatrixStructure", TagValue = "Blah2" });

        var isEqual = depositExifMetadata.SequenceEqual(metsExifMetadata, new ExifTagComparer());
        var t = depositExifMetadata.Except(metsExifMetadata, new ExifTagComparer());
        var arrayDeposit = t.ToArray();
        var t1 = metsExifMetadata.Except(depositExifMetadata, new ExifTagComparer());
        var arrayMets = t1.ToArray();

        var misMatches = new List<CombinedFile.FileMisMatch>();

        foreach (var exifItemDeposit in arrayDeposit.Select((value, i) => (value, i)))
        {
            var exifItemDepositTagName = exifItemDeposit.value.TagName;
            var exifItemDepositTagValue = exifItemDeposit.value.TagValue;
            var depositItemArrayIndex = exifItemDeposit.i;

            //test column only in deposit
            if (!isEqual && !arrayMets.Any())
            {
                continue;
            }

            var metsExifItem = arrayMets[depositItemArrayIndex];

            if (exifItemDepositTagName == null || metsExifItem.TagName == null || !string.Equals(exifItemDepositTagName,
                    metsExifItem.TagName, StringComparison.CurrentCultureIgnoreCase)) continue;

            if (!string.Equals(exifItemDepositTagValue, metsExifItem.TagValue, StringComparison.CurrentCultureIgnoreCase))
            {
                misMatches.Add(new CombinedFile.FileMisMatch(nameof(ExifMetadata), exifItemDepositTagName, exifItemDepositTagValue, metsExifItem.TagValue));
            }
        }

        var resultDeposit = depositExifMetadata.Where(p => metsExifMetadata.All(p2 => p2.TagName != p.TagName));
        misMatches.AddRange(resultDeposit.Select(depositField => new CombinedFile.FileMisMatch(nameof(ExifMetadata), depositField.TagName ?? string.Empty, depositField.TagValue, "field does not exist in METS")));
        
        var resultMets = metsExifMetadata.Where(p => depositExifMetadata.All(p2 => p2.TagName != p.TagName));
        misMatches.AddRange(resultMets.Select(metsField => new CombinedFile.FileMisMatch(nameof(ExifMetadata), metsField.TagName ?? string.Empty, metsField.TagValue, "field does not exist in deposit")));

        misMatches.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CompareTwoTagValueListsWithPropertyOnlyInDeposit()
    {
        //Duration only in deposit is only diff
        var depositExifMetadata = new List<ExifTag>();
        depositExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:43" });
        depositExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:45" });
        depositExifMetadata.Add(new ExifTag { TagName = "ColorVersion", TagValue = "Blah" });
        depositExifMetadata.Add(new ExifTag { TagName = "Duration", TagValue = "Blah" });

        var metsExifMetadata = new List<ExifTag>();
        metsExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:43" });
        metsExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:45" });
        metsExifMetadata.Add(new ExifTag { TagName = "ColorVersion", TagValue = "Blah" });

        var isEqual = depositExifMetadata.SequenceEqual(metsExifMetadata, new ExifTagComparer());
        var t = depositExifMetadata.Except(metsExifMetadata, new ExifTagComparer());
        var arrayDeposit = t.ToArray();
        var t1 = metsExifMetadata.Except(depositExifMetadata, new ExifTagComparer());
        var arrayMets = t1.ToArray();

        var misMatches = new List<CombinedFile.FileMisMatch>();

        foreach (var exifItemDeposit in arrayDeposit.Select((value, i) => (value, i)))
        {
            var exifItemDepositTagName = exifItemDeposit.value.TagName;
            var exifItemDepositTagValue = exifItemDeposit.value.TagValue;
            var depositItemArrayIndex = exifItemDeposit.i;

            //test column only in deposit
            if (!isEqual && !arrayMets.Any())
            {
                continue;
            }

            var metsExifItem = arrayMets[depositItemArrayIndex];

            if (exifItemDepositTagName == null || metsExifItem.TagName == null || !string.Equals(exifItemDepositTagName,
                    metsExifItem.TagName, StringComparison.CurrentCultureIgnoreCase)) continue;

            if (!string.Equals(exifItemDepositTagValue, metsExifItem.TagValue, StringComparison.CurrentCultureIgnoreCase))
            {
                misMatches.Add(new CombinedFile.FileMisMatch(nameof(ExifMetadata), exifItemDepositTagName, exifItemDepositTagValue, metsExifItem.TagValue));
            }
        }

        var resultDeposit = depositExifMetadata.Where(p => metsExifMetadata.All(p2 => p2.TagName != p.TagName));
        misMatches.AddRange(resultDeposit.Select(depositField => new CombinedFile.FileMisMatch(nameof(ExifMetadata), depositField.TagName ?? string.Empty, depositField.TagValue, "field does not exist in METS")));

        var resultMets = metsExifMetadata.Where(p => depositExifMetadata.All(p2 => p2.TagName != p.TagName));
        misMatches.AddRange(resultMets.Select(metsField => new CombinedFile.FileMisMatch(nameof(ExifMetadata), metsField.TagName ?? string.Empty, metsField.TagValue, "field does not exist in deposit")));

        misMatches.Count.Should().Be(1);
    }

    [Fact]
    public void CompareTwoTagValueListsWithPropertyOnlyInMets()
    {
        //Matrix structure only in mETS is only diff
        var depositExifMetadata = new List<ExifTag>();
        depositExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:43" });
        depositExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:45" });
        depositExifMetadata.Add(new ExifTag { TagName = "ColorVersion", TagValue = "Blah" });
        //depositExifMetadata.Add(new ExifTag { TagName = "Duration", TagValue = "Blah" });

        var metsExifMetadata = new List<ExifTag>();
        metsExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:43" });
        metsExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:45" });
        metsExifMetadata.Add(new ExifTag { TagName = "ColorVersion", TagValue = "Blah" });
        metsExifMetadata.Add(new ExifTag { TagName = "MatrixStructure", TagValue = "Blah2" });


        //var result = source.Intersect(compare);

        var isEqual = depositExifMetadata.SequenceEqual(metsExifMetadata, new ExifTagComparer());
        var t = depositExifMetadata.Except(metsExifMetadata, new ExifTagComparer());
        var arrayDeposit = t.ToArray();
        var t1 = metsExifMetadata.Except(depositExifMetadata, new ExifTagComparer());
        var arrayMets = t1.ToArray();

        var misMatches = new List<CombinedFile.FileMisMatch>();

        foreach (var exifItemDeposit in arrayDeposit.Select((value, i) => (value, i)))
        {
            var exifItemDepositTagName = exifItemDeposit.value.TagName;
            var exifItemDepositTagValue = exifItemDeposit.value.TagValue;
            var depositItemArrayIndex = exifItemDeposit.i;

            //test column only in deposit
            if (!isEqual && !arrayMets.Any())
            {
                continue;
            }

            var metsExifItem = arrayMets[depositItemArrayIndex];

            if (exifItemDepositTagName == null || metsExifItem.TagName == null || !string.Equals(exifItemDepositTagName,
                    metsExifItem.TagName, StringComparison.CurrentCultureIgnoreCase)) continue;

            if (!string.Equals(exifItemDepositTagValue, metsExifItem.TagValue, StringComparison.CurrentCultureIgnoreCase))
            {
                misMatches.Add(new CombinedFile.FileMisMatch(nameof(ExifMetadata), exifItemDepositTagName, exifItemDepositTagValue, metsExifItem.TagValue));
            }
        }

        var resultDeposit = depositExifMetadata.Where(p => metsExifMetadata.All(p2 => p2.TagName != p.TagName));
        misMatches.AddRange(resultDeposit.Select(depositField => new CombinedFile.FileMisMatch(nameof(ExifMetadata), depositField.TagName ?? string.Empty, depositField.TagValue, "field does not exist in METS")));

        var resultMets = metsExifMetadata.Where(p => depositExifMetadata.All(p2 => p2.TagName != p.TagName));
        misMatches.AddRange(resultMets.Select(metsField => new CombinedFile.FileMisMatch(nameof(ExifMetadata), metsField.TagName ?? string.Empty, metsField.TagValue, "field does not exist in deposit")));

        misMatches.Count.Should().Be(1);
    }

    [Fact]
    public void CompareTwoTagValueListsWithZeroMismatches()
    {
        var depositExifMetadata = new List<ExifTag>();
        depositExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:43" });
        depositExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:45" });
        depositExifMetadata.Add(new ExifTag { TagName = "Duration", TagValue = "1267" });

        var metsExifMetadata = new List<ExifTag>();
        metsExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:43" });
        metsExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:45" });
        metsExifMetadata.Add(new ExifTag { TagName = "Duration", TagValue = "1267" });

        var isEqual = depositExifMetadata.SequenceEqual(metsExifMetadata, new ExifTagComparer());

        isEqual.Should().Be(true);
    }

}
