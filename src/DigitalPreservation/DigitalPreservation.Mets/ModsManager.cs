using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Mods.V3;

namespace DigitalPreservation.Mets;


public static class ModsManager
{
    private static readonly XmlSerializerNamespaces Namespaces;
    
    static ModsManager()
    {
        Namespaces = new XmlSerializerNamespaces();
        Namespaces.Add("mods", "http://www.loc.gov/mods/v3");
        Namespaces.Add("xlink", "http://www.w3.org/1999/xlink");
    }
    
    public static ModsDefinition CreateRootMods(string name, string? language = null)
    {
        var modsDefinition = new ModsDefinition();
        var titleInfoDefinition = new TitleInfoDefinition();
        titleInfoDefinition.Title.Add(new StringPlusLanguage{ Value = name, Lang = language });
        modsDefinition.TitleInfo.Add(titleInfoDefinition);
        return modsDefinition;
    }

    
    public static List<string> GetAccessConditions(this ModsDefinition modsDefinition, string? type = null)
    {
        var result = new List<string>();
        foreach (var accessCondition in modsDefinition.AccessCondition)
        {
            if (type == null || accessCondition.Type == type && accessCondition.Text.Length > 0)
            {
                result.Add(accessCondition.Text[0]);
            }
        }
        return result;
    }
    
    public static void RemoveAccessConditions(this ModsDefinition modsDefinition, string? type = null)
    {
        foreach (var accessCondition in modsDefinition.AccessCondition.ToList())
        {
            if (type == null || accessCondition.Type == type)
            {
                modsDefinition.AccessCondition.Remove(accessCondition);
            }
        }
    }
    
    
    public static void AddAccessCondition(this ModsDefinition modsDefinition, string accessCondition, string type)
    {
        var accessConditionDefinition = new AccessConditionDefinition
        {
            Text = [accessCondition],
            Type = type
        };
        modsDefinition.AccessCondition.Add(accessConditionDefinition);
    }


    public static RecordInfo? GetRecordInfo(this ModsDefinition modsDefinition)
    {
        RecordInfo? recordInfo = null;
        // This will handle multiple RecordInfo elements, if we ever find any
        foreach (var recordInfoDefinition in modsDefinition.RecordInfo)
        {
            foreach (var recordIdentifierDefinition in recordInfoDefinition.RecordIdentifier)
            {
                recordInfo ??= new RecordInfo();
                recordInfo.RecordIdentifiers.Add(new RecordIdentifier
                {
                    Source =  recordIdentifierDefinition.Source,
                    Value = recordIdentifierDefinition.Value
                });
            }
        }
        return recordInfo;
    }
    
    
    public static void SetTitle(this ModsDefinition modsDefinition, string name, string? language = null)
    {
        modsDefinition.TitleInfo.Clear();
        var titleInfo = new TitleInfoDefinition();
        titleInfo.Title.Add(new StringPlusLanguage { Value = name, Lang = language });
        modsDefinition.TitleInfo.Add(titleInfo);
    }

    public static void SetRecordInfo(this ModsDefinition modsDefinition, RecordInfo recordInfo)
    {
        modsDefinition.RecordInfo.Clear();
        foreach (var recordIdentifier in recordInfo.RecordIdentifiers)
        {
            if (modsDefinition.RecordInfo.Count == 0)
            {
                modsDefinition.RecordInfo.Add(new RecordInfoDefinition());
            }
            modsDefinition.RecordInfo[0].RecordIdentifier.Add(new RecordIdentifierDefinition()
            {
                Source =  recordIdentifier.Source,
                Value = recordIdentifier.Value
            });
        }
    }
    
    public static XmlElement? GetXmlElement(ModsDefinition modsDefinition)
    {
        var serializer = new XmlSerializer(typeof(ModsDefinition));
        var doc = new XmlDocument();
        using (var xw = doc.CreateNavigator()!.AppendChild()) {
            serializer.Serialize(xw, modsDefinition, Namespaces);
        }
        return doc.DocumentElement;
    }

    public static ModsDefinition? GetModsForDmdId(DigitalPreservation.XmlGen.Mets.Mets mets, string dmdId, bool createDmd = false)
    {
        var dmd = mets.DmdSec.FirstOrDefault(x => x.Id == dmdId);
        if (dmd == null && createDmd)
        {
            dmd = new MdSecType
            {
                Id = dmdId,
                MdWrap = MakeDmdMdWrapForMods(new ModsDefinition())
            };
            mets.DmdSec.Add(dmd);
        }

        if (dmd == null)
        {
            return null;
        }
        
        if (dmd.MdWrap is { Mdtype: MdSecTypeMdWrapMdtype.Mods })
        {
            var modsXml = dmd.MdWrap.XmlData.Any?.FirstOrDefault();
            if (modsXml != null)
            {
                var serializer = new XmlSerializer(typeof(ModsDefinition));
                using var xmlNodeReader = new XmlNodeReader(modsXml);
                var des = serializer.Deserialize(xmlNodeReader);
                return des as ModsDefinition;
            }
        }

        return null;
    }

    /// <summary>
    /// The MODS for a div. When the div's DMDID is a genuine multi-token reference, the FIRST
    /// dmdSec a token resolves is treated as the div's editable MODS record; the platform only
    /// ever writes one descriptive record per div, so any further referenced dmdSecs are
    /// third-party content that read/write operations deliberately leave untouched.
    /// </summary>
    /// <param name="localPath">
    /// The div's deposit-relative path, where it has one. A dmdSec created for a physical div is
    /// identified from this path rather than from the div's ID, so a legacy raw-ID div gets a
    /// valid encoded DMD_ id instead of a second invalid one (issue #188). Null for logical divs,
    /// and for a physical div whose path could not be resolved - both fall back to deriving the
    /// ID from div.Id, which for a logical div is the client-supplied (NCName-validated) ID.
    /// </param>
    public static ModsDefinition? GetModsForDiv(DigitalPreservation.XmlGen.Mets.Mets mets, DivType div,
        bool createDmd = false, string? localPath = null)
    {
        if (div.Dmdid.Count == 0 && createDmd)
        {
            // There is no DMDID on this div
            var idPart = localPath != null
                ? localPath.ToMetsId()
                : div.Id.RemoveStart(Constants.PhysIdPrefix);
            div.Dmdid.Add(Constants.DmdIdPrefix + idPart);
        }
        // Resolve the DMDID tokens to the actual dmdSec where one exists; the id used for
        // lazy creation is the joined legacy form only when no dmdSec resolves (see IdRefs).
        var dmd = IdRefs.ResolveSingle(div.Dmdid, id => mets.DmdSec.FirstOrDefault(x => x.Id == id));
        return GetModsForDmdId(mets, dmd?.Id ?? IdRefs.Joined(div.Dmdid), createDmd);
    }

    
    /// <summary>
    /// Write the div's MODS record - the same single dmdSec <see cref="GetModsForDiv"/>
    /// resolves; other dmdSecs a genuine multi-token DMDID references are never mutated.
    /// </summary>
    public static void SetModsForDiv(DigitalPreservation.XmlGen.Mets.Mets mets, DivType div, ModsDefinition mods)
    {
        var dmd = IdRefs.ResolveSingle(div.Dmdid, id => mets.DmdSec.FirstOrDefault(x => x.Id == id));
        var normalised = dmd?.Id ?? IdRefs.Joined(div.Dmdid);

        // If clearing the last piece of metadata has left the MODS carrying nothing,
        // don't write an empty <mods/> shell wrapped in a redundant dmdSec — prune it.
        // Only do so when it is safe: the dmdSec must wrap MODS and nothing else.
        if (mods.IsEmpty() && TryRemoveRedundantDmd(mets, div, dmd))
        {
            return;
        }

        SetModsForDmdId(mets, normalised, mods);
    }

    /// <summary>
    /// Removes the resolved dmdSec together with the div's DMDID reference to it, but ONLY
    /// when it is safe: the dmdSec must carry nothing beyond the MODS we are clearing — no
    /// external mdRef, no admin links and no other identifying attributes. Only the reference
    /// to THIS dmdSec is dropped: a div with a genuine multi-token DMDID keeps its other
    /// references (and their dmdSecs) intact. Returns <c>true</c> if the dmdSec was removed
    /// (or there was nothing to remove), <c>false</c> if it had to be kept because it
    /// still carries other information.
    /// </summary>
    private static bool TryRemoveRedundantDmd(DigitalPreservation.XmlGen.Mets.Mets mets, DivType div, MdSecType? dmd)
    {
        if (dmd != null)
        {
            if (!DmdSecWrapsOnlyMods(dmd))
            {
                // The dmdSec carries information beyond the MODS we are clearing; leave it intact.
                return false;
            }

            // Parity with the deletion sites' shared-section guard: a dmdSec that another
            // div or file still references is genuinely shared, so clearing THIS div's MODS
            // drops only this div's reference and leaves the section (and its other
            // referrers) untouched.
            if (!MetsManager.CollectSectionReferences(mets, [div], null).Contains(dmd.Id))
            {
                mets.DmdSec.Remove(dmd);
            }
            IdRefs.RemoveReference(div.Dmdid, dmd.Id);

            // Sweep any sibling tokens that resolve nothing (the pre-existing Clear()
            // behaviour, scoped): they are dangling, while a token still naming a real
            // dmdSec keeps both its reference and its section.
            for (var i = div.Dmdid.Count - 1; i >= 0; i--)
            {
                var token = div.Dmdid[i];
                if (mets.DmdSec.All(d => d.Id != token))
                {
                    div.Dmdid.RemoveAt(i);
                }
            }
        }
        else
        {
            // Nothing resolved at all: every token dangles, so clearing them is cleanup
            // (and a no-op if DMDID was never set).
            div.Dmdid.Clear();
        }
        return true;
    }

    /// <summary>
    /// True when the dmdSec is just a wrapper around MODS, with no external reference,
    /// administrative links, group/status markers or extension attributes that would be
    /// lost if the section were removed.
    /// </summary>
    private static bool DmdSecWrapsOnlyMods(MdSecType dmd)
    {
        return dmd.MdRef == null
               && string.IsNullOrEmpty(dmd.Groupid)
               && string.IsNullOrEmpty(dmd.Status)
               && dmd.Admid.Count == 0
               && dmd.AnyAttribute.Count == 0;
    }

    /// <summary>
    /// True when a MODS record carries no information at all: every repeatable element
    /// collection is empty and none of the identifying/version attributes are set.
    /// Used to decide whether a dmdSec has become redundant after metadata is cleared.
    /// </summary>
    public static bool IsEmpty(this ModsDefinition mods)
    {
        return mods.Abstract.Count == 0
               && mods.AccessCondition.Count == 0
               && mods.Classification.Count == 0
               && mods.Extension.Count == 0
               && mods.Genre.Count == 0
               && mods.Identifier.Count == 0
               && mods.Language.Count == 0
               && mods.Location.Count == 0
               && mods.Name.Count == 0
               && mods.Note.Count == 0
               && mods.OriginInfo.Count == 0
               && mods.Part.Count == 0
               && mods.PhysicalDescription.Count == 0
               && mods.RecordInfo.Count == 0
               && mods.RelatedItem.Count == 0
               && mods.Subject.Count == 0
               && mods.TableOfContents.Count == 0
               && mods.TargetAudience.Count == 0
               && mods.TitleInfo.Count == 0
               && mods.TypeOfResource.Count == 0
               && string.IsNullOrEmpty(mods.Id)
               && string.IsNullOrEmpty(mods.Idref)
               && mods.Version == null;
    }

    private static void SetModsForDmdId(DigitalPreservation.XmlGen.Mets.Mets mets, string dmdId, ModsDefinition mods)
    {
        var dmd = mets.DmdSec.Single(x => x.Id == dmdId)!;
        dmd.MdWrap = MakeDmdMdWrapForMods(mods);
    }

    private static MdSecTypeMdWrap MakeDmdMdWrapForMods(ModsDefinition mods)
    {
        return new MdSecTypeMdWrap
        {
            Mdtype = MdSecTypeMdWrapMdtype.Mods,
            XmlData = new MdSecTypeMdWrapXmlData
            {
                Any = { GetXmlElement(mods) }
            }
        };
    }
}