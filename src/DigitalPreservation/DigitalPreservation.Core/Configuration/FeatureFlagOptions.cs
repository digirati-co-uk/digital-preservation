namespace DigitalPreservation.Core.Configuration;

/// <summary>
/// Every feature flag a host reads under the "FeatureFlags" configuration section, bound once
/// instead of each flag being read as a raw, typo-prone config string at its point of use (issue
/// #233 item 2, widened by issue #388's sweep to cover every flag of this shape, not just the two
/// METS-ID-normalisation ones). A given host populates only the flags that apply to it; the rest
/// default to false.
/// </summary>
/// <remarks>
/// Does not replace <c>RequireFeatureFlagAttribute</c>: that attribute gates an arbitrary, named
/// flag per endpoint (e.g. <c>[RequireFeatureFlag("EnableIiifMediaEndpoints")]</c>), which a fixed
/// set of typed properties can't express. This is for the flags read once, by a fixed name, at a
/// specific call site - a check inside an action body, or a choice between DI registrations at
/// startup.
/// </remarks>
public class FeatureFlagOptions
{
    public const string FeatureFlags = "FeatureFlags";

    /// <summary>Preservation.API, Storage.API, Storage.API.Importer: skip JWT auth entirely (local dev only).</summary>
    public bool DisableAuth { get; set; }

    /// <summary>Preservation.API: honour a METS-ID-normalisation import job's request to suppress its Activity Stream event.</summary>
    public bool EnableMetsIdNormalisation { get; set; }

    /// <summary>DigitalPreservation.UI: offer "Normalise METS IDs" from a deposit's Actions menu.</summary>
    public bool ShowNormaliseMetsIds { get; set; }

    /// <summary>DigitalPreservation.UI: show pipeline-related deposit actions.</summary>
    public bool ShowPipeline { get; set; }

    /// <summary>Storage.API: run import jobs in-process instead of via SQS.</summary>
    public bool UseLocalHostedServiceForImport { get; set; }

    /// <summary>Storage.API: run exports in-process instead of via SQS.</summary>
    public bool UseLocalHostedServiceForExport { get; set; }

    /// <summary>Pipeline.API: run pipeline jobs in-process instead of via SQS.</summary>
    public bool UseLocalHostedServiceForPipeline { get; set; }
}
