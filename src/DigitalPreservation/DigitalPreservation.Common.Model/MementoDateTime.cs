using System.Globalization;

namespace DigitalPreservation.Common.Model;

public static class MementoDateTime
{
    public const string Format = "yyyyMMddHHmmss";

    public static DateTime DateTimeFromMementoTimestamp(this string mementoTimestamp)
    {
        return DateTime.ParseExact(mementoTimestamp, Format, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }

    public static string ToMementoTimestamp(this DateTime dt)
    {
        return dt.ToString(Format);
    }

    public static string ToRFC1123(this DateTime dt)
    {
        return dt.ToString(CultureInfo.InvariantCulture.DateTimeFormat.RFC1123Pattern);
    }

    public static string ToRFC1123(this string mementoTimestamp)
    {
        return DateTimeFromMementoTimestamp(mementoTimestamp).ToRFC1123();
    }
}