namespace DigitalPreservation.Mets.AccessConditions;

/// <summary>
/// Thrown by an <see cref="IAccessConditionProfileProvider"/> that EXPECTS a profile but cannot get
/// one - the UI, when the Preservation API cannot be reached and nothing was fetched earlier. It
/// is distinct from returning null, which means "this installation derives nothing": a write that
/// went ahead with nothing derived would leave every existing derived value as it was, so a
/// changed restriction could be saved - and preserved - beside a tier computed from the old one
/// (review of #434). <see cref="MetsManager.WriteMets"/> turns it into a failed write instead.
/// </summary>
public class AccessConditionProfileUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
