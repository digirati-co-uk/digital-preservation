using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit.Combined;

public abstract class CombinedBase(string? relativePath = null)
{
    protected readonly string? RelativePath = relativePath;

    protected abstract ResourceBase? InMetsBase { get; }

    public List<string> AccessRestrictions => InMetsBase?.AccessRestrictions ?? [];
    public List<string> EffectiveAccessRestrictions => InMetsBase?.EffectiveAccessRestrictions ?? [];
    public Uri? RightsStatement => InMetsBase?.RightsStatement;
    public Uri? EffectiveRightsStatement => InMetsBase?.EffectiveRightsStatement;
    public RecordInfo? RecordInfo => InMetsBase?.RecordInfo;
    public RecordInfo? EffectiveRecordInfo => InMetsBase?.EffectiveRecordInfo;
}

public abstract class CombinedBase<T>(string? relativePath = null) : CombinedBase(relativePath)
    where T : WorkingBase
{
    protected abstract T? InDeposit { get; }
    protected abstract T? InMets { get; }

    protected override ResourceBase? InMetsBase => InMets;

    public virtual string? LocalPath
    {
        get
        {
            if (RelativePath == null)
            {
                return InDeposit?.LocalPath ?? InMets?.LocalPath;
            }
            if (InDeposit == null)
            {
                return InMets?.LocalPath;
            }
            if (InDeposit.LocalPath.StartsWith($"{RelativePath}/"))
            {
                return InDeposit.LocalPath.RemoveStart($"{RelativePath}/");
            }
            return "../" + InDeposit.LocalPath;
        }
    }

    public Whereabouts Whereabouts
    {
        get
        {
            if (InDeposit is not null && InMets is not null)
            {
                return Whereabouts.Both;
            }

            if (InDeposit is not null)
            {
                if (RelativePath.HasText() && !InDeposit.LocalPath.StartsWith(RelativePath))
                {
                    return Whereabouts.Extra;
                }
                return Whereabouts.Deposit;
            }

            if (InMets is not null)
            {
                return Whereabouts.Mets;
            }

            return Whereabouts.Neither;
        }
    }
}
