namespace Preservation.API.IIIF;

public interface ITokenService
{
    string GetToken(string key);
    string? GetKey(string token);
}
