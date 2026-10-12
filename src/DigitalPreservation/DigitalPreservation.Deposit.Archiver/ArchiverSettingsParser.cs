namespace DigitalPreservation.Deposit.Archiver;

public static class ArchiverSettingsParser
{
    public static int ParseLastModifiedMonths(string? rawLastModifiedMonths)
    {
        var months = Math.Abs(Convert.ToInt32(rawLastModifiedMonths));

        if (months == 0)
        {
            throw new InvalidOperationException("LAST_MODIFIED_MONTHS must be a non-zero value.");
        }

        return months;
    }

    public static int ParseBatchSize(string? rawBatchSize)
    {
        var batchSize = Convert.ToInt32(rawBatchSize);

        if (batchSize < 1)
        {
            throw new InvalidOperationException("BATCH_SIZE must be a positive integer.");
        }

        return batchSize;
    }
}
