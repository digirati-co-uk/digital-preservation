using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace DigitalPreservation.Common.Model.Mets;

public interface IPremisEventManager<in T>
    where T : class
{
    EventComplexType Create(T metadata);
    void Patch(EventComplexType eventComplexType, T metadata);
    string Serialise(EventComplexType eventComplexType);
    XmlElement? GetXmlElement(EventComplexType eventComplexType);
}
