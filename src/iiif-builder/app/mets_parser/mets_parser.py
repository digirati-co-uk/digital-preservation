from datetime import datetime, timezone

import lxml.etree as etree
from app.mets_parser.mets_wrapper import MetsWrapper
from app.mets_parser.util import get_parent, get_slug
from app.mets_parser.working_filesystem import WorkingDirectory, WorkingFile
from app.mets_parser.vocab import *

def get_mets_wrapper_from_file_like_object(file_path_or_object):
    mets_doc = etree.parse(file_path_or_object)
    root = mets_doc.getroot()
    mets_wrapper = build_mets_wrapper(root)
    return mets_wrapper


def get_mets_wrapper_from_string(xml_string):
    root = etree.fromstring(xml_string)
    mets_wrapper = build_mets_wrapper(root)
    return mets_wrapper


def build_mets_wrapper(root)->MetsWrapper:
    physical_structure = WorkingDirectory()
    physical_structure.local_path = ""
    physical_structure.name = "__ROOT"
    physical_structure.modified = datetime.now(timezone.utc)

    mets_wrapper = MetsWrapper()
    mets_wrapper.physical_structure = physical_structure
    for amd_sec in root.findall(f".//{{{mets}}}amdSec[@ID]"):
        # print("mapping amd_sec with ID " + amd_sec.get("ID"))
        mets_wrapper.amd_map[amd_sec.get("ID")] = amd_sec

    file_sec = root.find(f".//{{{mets}}}fileSec")
    for f in file_sec.findall(f".//{{{mets}}}file[@ID]"):
        # print("mapping file with ID " + f.get("ID"))
        mets_wrapper.file_map[f.get("ID")] = f

    for tech_md in root.findall(f".//{{{mets}}}techMD[@ID]"):
        # print("mapping tech_md with ID " + tech_md.get("ID"))
        mets_wrapper.tech_map[tech_md.get("ID")] = tech_md

    populate_from_mets(mets_wrapper, root)
    return mets_wrapper

# python version of MetsParser::PopulateFromMets


def populate_from_mets(mets_wrapper:MetsWrapper, root):
    mods_title = find_value(root, f".//{{{mods}}}title")
    mods_name = find_value(root, f".//{{{mods}}}name")
    name = mods_title or mods_name
    mets_wrapper.name = name

    agent = root.find(f".//{{{mets}}}agent")
    if agent is not None:
        mets_wrapper.agent = find_value(agent, f".//{{{mets}}}name")

    physical_struct_map = None
    for sm in root.findall(f".//{{{mets}}}structMap"):
        type_attr = sm.attrib.get("TYPE", None)
        if type_attr is not None:
            if type_attr.lower() == "physical":
                physical_struct_map = sm
                break
            if type_attr.lower() == "logical":
                continue
        if physical_struct_map is None:
            # This may get overwritten if we find a better one in the loop
            # EPrints METS files structMap don't have type
            physical_struct_map = sm

    if physical_struct_map is None:
        raise Exception("METS file must have a physical structMap")

    """
    // Now walk down the structMap
    // Each div either contains 1 (or sometimes more) mets:fptr, or it contains child DIVs.
    // If a DIV containing a mets:fptr has a LABEL (not ORDERLABEL) then that is the name of the file
    // If those DIVs have TYPE="Directory" and a LABEL, that gives us the name of the directory.
    // We need to see the path of the file, too.

    // A DIV TYPE="Directory" should never directly contain a file

    // GOOBI METS at Wellcome contain images and ALTO in the same DIV; the ADM_ID is for the Image not the ALTO.
    // Not sure how to be formal about that.
    """

    parent = sm

    # This relies on all directories having labels not just some
    directory_labels = []
    process_child_struct_divs(mets_wrapper, root, parent, directory_labels)

    for file in mets_wrapper.files:
        folder = mets_wrapper.physical_structure.find_directory(get_parent(file.local_path), False)
        if folder is None:
            raise Exception("Our folder logic is wrong")
        folder.files.append(file)


def process_child_struct_divs(mets_wrapper:MetsWrapper, root, parent, directory_labels):
    """
    // We want to create MetsFileWrapper::PhysicalStructure (WorkingDirectories and WorkingFiles).
    // We can traverse the physical structmap, finding div type=Directory and div type=File
    // But we have a problem - if a directory has no files in it, we don't know the path of that
    // directory. If it has grandchildren we can eventually populate it. But if not we will have
    // to rely on the AMD premis:originalName as the local path.
    """
    # print("entering process_child_struct_divs with parent " + str(parent.tag))
    # print(parent.attrib.keys)
    # print("loop through child divs of " + str(parent.tag))
    child_divs = (el for el in parent if el.tag == f"{{{mets}}}div")
    for div in child_divs:
        type_ = div.get("TYPE", "").lower()
        label = div.get("LABEL", "").lower()
        if type_ == "directory":
            if not label:
                raise Exception("If a mets:div has type Directory, it must have a label")
            directory_labels.append(label)
            adm_id = div.get("ADMID", None)
            if adm_id:
                amd = mets_wrapper.amd_map.get(adm_id, None)
                if amd is not None:
                    original_name = amd.find(f".//{{{premis}}}originalName")
                    if original_name is not None:
                        # Only in this scenario can we create a directory
                        working_directory = mets_wrapper.physical_structure.find_directory(original_name.text, True)
                        if not working_directory.name:
                            name_from_path = get_slug(original_name.text)
                            name_from_label = None
                            if len(directory_labels) > 0:
                                name_from_label = directory_labels.pop()
                            working_directory.name = name_from_label or name_from_path
                            working_directory.local_path = original_name.text

        have_used_adm_id_already = False
        # print("loop through child fptr of " + str(div.tag))
        child_fptrs = (el for el in div if el.tag == f"{{{mets}}}fptr")
        for fptr in child_fptrs:
            # print("starting fptr " + str(fptr.tag))
            adm_id = div.get("ADMID", None)
            # Goobi METS has the ADMID on the mets:div. But that means we can use it only once!
            # Going to make an assumption for now that the first encountered mets:fptr is the one that gets the ADMID
            file_id = fptr.get("FILEID", None)
            file_el = mets_wrapper.file_map.get(file_id)
            mime_type = file_el.get("MIMETYPE", None)
            flocat = file_el.find(f".//{{{mets}}}FLocat").get(f"{{{xlink}}}href")
            if adm_id is None:
                adm_id = file_el.get("ADMID", None)
                have_used_adm_id_already = False
            digest = None
            size = 0
            if not have_used_adm_id_already:
                tech_md = mets_wrapper.tech_map.get(adm_id, None)
                if tech_md is None:
                    tech_md = mets_wrapper.amd_map[adm_id]
                fixity = tech_md.find(f".//{{{premis}}}fixity")
                if fixity is not None:
                    algorithm_el = fixity.find(f".//{{{premis}}}messageDigestAlgorithm")
                    if algorithm_el is not None:
                        algorithm = algorithm_el.text.lower().replace("-", "")
                        if algorithm == "sha256":
                            digest = fixity.find(f".//{{{premis}}}messageDigest").text
                size_el = tech_md.find(f".//{{{premis}}}size")
                if size_el is not None:
                    size = int(size_el.text)
                have_used_adm_id_already = True

            parts = flocat.split('/')

            file = WorkingFile()
            file.content_type = mime_type or "dlip/not-identified"
            file.local_path = flocat
            file.digest = digest
            file.size = size
            file.name = label or parts[-1]

            mets_wrapper.files.append(file)

            if len(parts) > 0:
                walk_back = len(parts)
                while walk_back > 1:
                    parent_directory = '/'.join(parts[:walk_back-1])
                    working_directory = mets_wrapper.physical_structure.find_directory(parent_directory, True)
                    if not working_directory.name:
                        name_from_path = parts[walk_back-2]
                        name_from_label = None
                        if len(directory_labels) > 0:
                            name_from_label = directory_labels.pop()
                        working_directory.name = name_from_label or name_from_path
                        working_directory.local_path = parent_directory
                    walk_back = walk_back - 1
            # print("finished fptr " + str(div.tag))

        process_child_struct_divs(mets_wrapper, root, div, directory_labels)



def find_value(element, expr):
    found = element.find(expr)
    if found is None:
        return None
    return found.text



