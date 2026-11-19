using System.Security.Cryptography;

namespace DigitalPreservation.Utils;

public static class Checksum
{
    public static string? HashFromStream(Stream openedStream, Func<Stream, byte[]> hash)
    {
        try
        {
            if (openedStream.CanSeek)
            {
                openedStream.Position = 0;
            }
            // Compute the hash of the fileStream.
            byte[] hashValue = hash(openedStream);
            // Write the name and hash value of the file to the console.
            return FromByteArray(hashValue);
        }
        catch (IOException e)
        {
            Console.WriteLine($"I/O Exception: {e.Message}");
        }
        catch (UnauthorizedAccessException e)
        {
            Console.WriteLine($"Access Exception: {e.Message}");
        }
        return null;
    }

    public static string? Sha256FromFile(FileInfo fileInfo)
    {
        // Read-only and shared: the parameterless Open(FileMode.Open) defaults to
        // FileAccess.ReadWrite + FileShare.None, which fails on read-only files and
        // throws if anything else has the file open (e.g. concurrent parses of the same METS).
        using FileStream fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        return HashFromStream(fileStream, SHA256.HashData);
    }

    public static string? Sha512FromFile(FileInfo fileInfo)
    {
        using FileStream fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        return HashFromStream(fileStream, SHA512.HashData);
    }


    public static string? Sha256FromStream(Stream stream) => HashFromStream(stream, SHA256.HashData);
    public static string? Sha512FromStream(Stream stream) => HashFromStream(stream, SHA512.HashData);

    public static string FromByteArray(byte[] hashValue) => Convert.ToHexStringLower(hashValue);
}
