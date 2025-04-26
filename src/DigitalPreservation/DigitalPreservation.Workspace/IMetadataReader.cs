using DigitalPreservation.Common.Model.Transit;

namespace DigitalPreservation.Workspace;

public interface IMetadataReader
{
    void Decorate(WorkingBase workingBase);
}