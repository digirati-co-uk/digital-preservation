using System.Text;
using DigitalPreservation.Common.Model.Constants;
using OtpNet;

namespace DigitalPreservation.Common.Model;

/// <summary>
/// Generates and verifies totp codes
/// </summary>
public static class ToptHelper
{
    private static readonly string Seed = AuthConstants.ActivityApiTotpSeed;
    private static readonly Totp Totp;

    static ToptHelper()
    {
        Totp = new Totp(
            Encoding.ASCII.GetBytes(Seed),
            mode: OtpHashMode.Sha512,
            step: 300, //seconds
            totpSize: 8);
    }
    
    public static string? GetTotp =>
        Totp.ComputeTotp();

    public static bool Verify(string? code) =>
        code is not null && Totp.VerifyTotp(code, out _);
}