using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;

namespace DigitalPreservation.Core.Tests.Lists;
public class ListsComparer
{
    [Fact]
    public void CompareTwoPersonLists()
    {
        var source = new List<Person>() { new("Ken", "Nakamura"), new("Nozomi", "Nakamura") };
        var compare = new List<Person>() { new("Ken", "Nakamura"), new("Keiko", "Nakamura") };
        //var result = source.Intersect(compare);

        bool isEqual = source.SequenceEqual(compare, new PersonComparer());
        var t = source.Except(compare, new PersonComparer());
        var t1 = compare.Except(source, new PersonComparer());
    }

    [Fact]
    public void CompareTwoTagValueLists()
    {
        var depositExifMetadata = new List<ExifTag>();
        depositExifMetadata.Add(new ExifTag {TagName = "ExifToolVersionNumber", TagValue = "13:42"});
        depositExifMetadata.Add(new ExifTag { TagName = "ExifToolVersionNumber", TagValue = "13:44" });
        depositExifMetadata.Add(new ExifTag { TagName = "ColorVersion", TagValue = "Blah" });
        depositExifMetadata.Add(new ExifTag { TagName = "Duration", TagValue = "Blah" });

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
    }

}

public class Person
{
    public Person(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }
    public string? FirstName { get; set; }
    public string? LastName { get; set; } 
}
public class PersonComparer : IEqualityComparer<Person>
{
    public bool Equals(Person x, Person y)
    {
        //Check whether the compared objects reference the same data. 
        if (ReferenceEquals(x, y))
            return true;

        //Check whether any of the compared objects is null. 
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
            return false;

        return string.Equals(x.FirstName, y.FirstName, StringComparison.OrdinalIgnoreCase) && x.LastName == y.LastName;
    }

    public int GetHashCode(Person person)
    {
        //Check whether the object is null 
        if (ReferenceEquals(person, null))
            return 0;

        //Get hash code for the name field if it is not null
        int firstNameHashCode = !string.IsNullOrEmpty(person.FirstName) ? 0 : person?.FirstName?.GetHashCode() ?? 0;
        int lastNameHashCode = !string.IsNullOrEmpty(person?.LastName) ? 0 : person?.LastName?.GetHashCode() ?? 0;
        // Get hash code for marks also if its not 0

        return firstNameHashCode ^ lastNameHashCode;
    }
}

public class ExifTagComparer : IEqualityComparer<ExifTag>
{
    public bool Equals(ExifTag? x, ExifTag? y)
    {
        //Check whether the compared objects reference the same data. 
        if (ReferenceEquals(x, y))
            return true;

        //Check whether any of the compared objects is null. 
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
            return false;

        return string.Equals(x.TagName, y.TagName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.TagValue, y.TagValue, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(ExifTag exifTag)
    {
        //Check whether the object is null 
        if (ReferenceEquals(exifTag, null))
            return 0;

        //Get hash code for the name field if it is not null
        var tagNameHashCode = !string.IsNullOrEmpty(exifTag.TagName) ? 0 : exifTag?.TagName?.GetHashCode() ?? 0;
        var tagValueHashCode = exifTag != null && !string.IsNullOrEmpty(exifTag.TagValue)
            ? 0
            : exifTag?.TagValue?.GetHashCode() ?? 0;
        // Get hash code for marks also if its not 0

        return tagNameHashCode ^ tagValueHashCode;
    }
}
