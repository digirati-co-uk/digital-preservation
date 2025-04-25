import collections

from logzero import logger

from app import settings
from app.mets_parser.mets_wrapper import MetsWrapper
from app.mets_parser.working_filesystem import WorkingDirectory
from app.result import Result


def add_descriptive_metadata_to_manifest(manifest, descriptive_metadata) -> Result:
    # descriptive_metadata is a Dictionary in the MVP implementation
    # See https://dev.azure.com/universityofleeds/Library/_wiki/wikis/Library.wiki/4864/Present-IIIF(new)-manifests-to-Website

    try:
        data = descriptive_metadata["data"]
        # bug in test data
        title = data.get("Title", None) or data.get("title", None) or "[NO TITLE]"
        manifest["label"] = { "en": [ title ] }
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
        if rights is not None and len(rights) > 0 and rights[0]:
            manifest["rights"] = rights[0]

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
        return Result.success(None)

    except Exception as e:
        msg = f"Could not call catalogue API: {e}"
        logger.error(msg)
        return Result(False, msg)


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


def add_painted_resources(manifest, archival_group, mets:MetsWrapper, canvas_id_prefix, asset_prefix) -> Result:

    # Note that there is no items[] in our manifest.
    # For IIIF-Builder MVP we are going to do EVERYTHING with paintedResources.
    if "items" in manifest:
        del manifest["items"]
    manifest["paintedResources"] = []
    working_dir = mets.physical_structure
    if add_painted_resources_from_working_dir(manifest["paintedResources"], working_dir, archival_group, canvas_id_prefix, asset_prefix, canvas_index=0):
        return Result(manifest)
    return Result(False, f"Could not turn METS file information into painted resources: (error message)")


def add_painted_resources_from_working_dir(painted_resources, working_dir:WorkingDirectory, archival_group, canvas_id_prefix, asset_prefix, canvas_index):
    """
        In our initial iiif-builder flow, we will ONLY use `paintedResources` and never send
        the Manifest with an `items` property. This means that IIIF-CS will generate and manage
        the IIIF Canvases. This means we can't put arbitrary properties on the Canvases.
        But we can do enough to cover basic requirements; the canvasPainting resource
        allows us to give the canvas a label. It also allows to construct canvases with
        Choice annotation bodies, and multiple images on the same Canvas, and targets that
        are not the whole Canvas. These extras are not needed for EPrints and are not shown here.

        The following code constructs the Manifest paintedResources as we want them to be,
        regardless of what's there already (if anything). In the step after this we will distinguish
        between assets that we want to be there and don't need any further work, and assets
        that we want to re-process (most likely because the file at a particular relative path changed in
        an update to an archival group).

        This recursively walks the folder structure.
        In a later iteration, we can add IIIF Ranges to the Manifest here, to reflect the folder
        structure.
    """
    logger.info(f"adding painted resources from working dir: {working_dir.local_path}")
    logger.info(f"{working_dir.local_path} has {len(working_dir.files)} files and {len(working_dir.directories)} directories")

    for f in working_dir.files:

        # You can also obtain the origin by traversing the ArchivalGroup Container hierarchy, following
        # the path given by f.local_path. This gives you the S3 URI directly (the origin property
        # of the binary at the end of the path) but is more code otherwise.
        origin = f"{archival_group["origin"]}/{archival_group["storageMap"]["files"][f.local_path]["fullPath"]}"
        logger.info(f"file {f.local_path} has origin {origin}")
        logger.info(f"file {f.local_path} has content type {f.content_type}")

        # do we want to do this for starters?
        if not f.content_type.startswith("image"):
            logger.info(f"skipping file {f.local_path} because it is not an image")
            continue

        single_path_file_id = f.local_path.replace('/', '_')
        logger.info(f"file {f.local_path} will use iiif-cs id {single_path_file_id}")
        painted_resource = {
            "canvasPainting": {
                # canvasId is optional but gives iiif-b more control. IIIF-CS will mint its own otherwise.
                "canvasId": f"{canvas_id_prefix}{single_path_file_id}",
                "canvasOrder": canvas_index,        # optional, will use array order otherwise
                "label": { "en" : [ f.name ] }      # e.g., page numbers... e.g., "xvii", "37r", etc.
            },
            "asset": {
                "id": f"{asset_prefix}{single_path_file_id}", # use the file path as the ID. Use pid to scope to the manifest,
                "mediaType": f.content_type, #                            because all in same space
                "space": settings.IIIF_CS_ASSET_SPACE_ID,
                "origin": origin
            }
        }
        logger.info(f"appending painted resource with canvasId: {painted_resource['canvasPainting']['canvasId']}, asset.id: {painted_resource['asset']['id']}")
        painted_resources.append(painted_resource)

        canvas_index = canvas_index + 1

    for d in working_dir.directories:
        logger.info(f"recursing into directory {d.local_path}")
        if not add_painted_resources_from_working_dir(painted_resources, d, archival_group, canvas_id_prefix, asset_prefix, canvas_index):
            return False

    return True



