using System.Security.Cryptography;
using System.Text;

namespace DigitalPreservation.Utils;

public class Checksum
{
    public static string? HashFromStream(Stream openedStream, HashAlgorithm hashAlgorithm)
    {
        try
        {
            if (openedStream.CanSeek)
            {
                openedStream.Position = 0;
            }
            // Compute the hash of the fileStream.
            byte[] hashValue = hashAlgorithm.ComputeHash(openedStream);
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
        using SHA256 sha256 = SHA256.Create();
        using FileStream fileStream = fileInfo.Open(FileMode.Open);
        return HashFromStream(fileStream, sha256);
    }

    public static string? Sha512FromFile(FileInfo fileInfo)
    {
        using SHA512 sha512 = SHA512.Create();
        using FileStream fileStream = fileInfo.Open(FileMode.Open);
        return HashFromStream(fileStream, sha512);
    }


    public static string? Sha256FromStream(Stream stream)
    {
        using SHA256 sha256 = SHA256.Create();
        return HashFromStream(stream, sha256);
    }
    public static string? Sha512FromStream(Stream stream)
    {
        using SHA512 sha512 = SHA512.Create();
        return HashFromStream(stream, sha512);
    }

    public static string FromByteArray(byte[] hashValue)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < hashValue.Length; i++)
        {
            sb.Append(hashValue[i].ToString("x2"));
        }
        return sb.ToString();
    }
}
