import collections

from metsrw import METSDocument

from app.mets_parser.mets_wrapper import MetsWrapper
from app.result import Result


def add_descriptive_metadata_to_manifest(manifest, descriptive_metadata):
    # descriptive_metadata is a Dictionary in the MVP implementation
    # See https://dev.azure.com/universityofleeds/Library/_wiki/wikis/Library.wiki/4864/Present-IIIF(new)-manifests-to-Website
    data = descriptive_metadata["data"]
    manifest["label"] = { "en": [ data["Title"] ] }
    add_metadata_label_and_value(manifest, data, "Shelfmark", "none")
    add_metadata_label_and_value(manifest, data, "Object Number", "none")
    add_metadata_label_and_value(manifest, data, "Date", "none")
    add_metadata_label_and_value(manifest, data, "Description", "en") # is it always though?
    add_metadata_label_and_value(manifest, data, "Dimensions", "none")
    add_metadata_label_and_value(manifest, data, "Notes", "en")
    add_metadata_label_and_value(manifest, data, "Collections", "en")
    add_metadata_label_and_value(manifest, data, "Credit Line", "none")
    add_metadata_label_and_value(manifest, data, "Attribution", "en")
    add_metadata_label_and_value(manifest, data, "Medium", "en")
    add_metadata_label_and_value(manifest, data, "Technique", "en")
    add_metadata_label_and_value(manifest, data, "Support", "en")
    add_metadata_label_and_value(manifest, data, "Creators", "en")

    rights = data.get("Rights", None)
    if rights is not None:
        manifest["rights"] = rights

    homepage = data.get("Homepage", None)
    if homepage is not None:
        manifest["homepage"] = [
            {
                "id": homepage,
                "type": "Text",
                "format": "text/html",
                "language": [ "en" ],
                "label": { "en": [ f"Homepage for {manifest["label"]["en"][0]}" ] }
            }
        ]


def add_metadata_label_and_value(manifest, data_dict, key, lang="en"):
    metadata = manifest.get("metadata", None)
    value = data_dict.get(key, None)
    if value is None:
        return

    if metadata is None:
        metadata = []
        manifest["metadata"] = metadata

    if isinstance(value, collections.abc.Sequence) and not isinstance(value, str):
        values = value
    else:
        values = [value]

    if len(values) == 0:
        return

    metadata.append({
        "label": { "en" : [ key ] },
        "value": { lang: values }
    })


def add_painted_resources(manifest, archival_group, mets:MetsWrapper) -> Result:

    working_dir = mets.physical_structure
    return Result(False, f"Could not turn METS file information into painted resources: (error message)")
