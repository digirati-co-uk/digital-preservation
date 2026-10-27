namespace Preservation.API.IIIF;

public interface ITokenService
{
    string GetToken(string key);
    string? GetKey(string token);

    /// <summary>
    /// True when <paramref name="token"/> is still valid and was minted for exactly
    /// <paramref name="depositId"/> - the one check every IIIF session-token endpoint must make
    /// before doing anything else (issue #388 item 2.9).
    /// </summary>
    bool IsValidForDeposit(string token, string depositId);
}
