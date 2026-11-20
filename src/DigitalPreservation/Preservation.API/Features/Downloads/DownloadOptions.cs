using System.Security.Claims;

namespace Preservation.API.Features.Downloads;

public class DownloadOptions
{
    public const string Downloads = "Downloads";

    /// <summary>In <see cref="Roles"/>, lets every caller download, whatever roles they have.</summary>
    public const string AnyCaller = "*any*";

    /// <summary>
    /// The Entra app roles that may download preserved files; a caller needs one of them. Empty
    /// means nobody may. <see cref="AnyCaller"/> is for use until the role exists in Entra.
    /// </summary>
    public string[] Roles { get; set; } = [];

    /// <summary>How long a copy stays available before it is removed.</summary>
    public int LifetimeDays { get; set; } = 7;

    /// <summary>How long to wait for a copy to appear before reporting that it failed.</summary>
    public int CopyTimeoutMinutes { get; set; } = 15;

    public bool Permits(ClaimsPrincipal caller) =>
        Roles.Contains(AnyCaller) || Roles.Any(role => caller.IsInRole(role) || caller.HasClaim("roles", role));
}
