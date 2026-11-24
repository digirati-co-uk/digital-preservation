namespace DigitalPreservation.Core.Auth;

/// <summary>
/// Marks an endpoint that <see cref="AuthFilterIdentifier"/> does not refuse under the RFC-0001
/// Phase 4 flags (<c>RequireKnownCaller</c>, <c>RequireCallerRole</c>, issue #293). The caller must
/// still present a valid token. It is for <c>/whoami</c> only: the endpoint that tells a caller why
/// it is being refused has to answer the callers that are refused.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ExemptFromCallerEnforcementAttribute : Attribute;
