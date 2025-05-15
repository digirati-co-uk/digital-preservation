namespace DigitalPreservation.Core.Tests.Strings;

public class MutateUriTests
{
        
    [Fact]
    public void Mutate_Port_Behaviour()
    {
        var preservationHost = "https://preservation.com";
        var uri = new Uri("https://preservation.com/aa/bb/cc");
        var storageUri = new Uri("https://storage.com");
        var newUri = uri;
        if (uri.ToString().StartsWith(preservationHost))
        {
            var builder = new UriBuilder(uri)
            {
                Host = storageUri.Host,
                Port = storageUri.Port,
                Scheme = storageUri.Scheme
            };
            newUri = builder.Uri;
        }
        newUri.ToString().Should().Be(    "https://storage.com/aa/bb/cc");
        // newUri. OriginalString.Should().Be("https://storage.com:443/aa/bb/cc");
    }
    
}