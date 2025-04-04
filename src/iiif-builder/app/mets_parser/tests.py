from app.mets_parser.mets_parser import get_mets_wrapper_from_file_like_object

if __name__ == '__main__':
    wrapper_1 = get_mets_wrapper_from_file_like_object("test_fixtures/eprints/10315.METS.xml")
    print(wrapper_1.physical_structure)
    wrapper_2 = get_mets_wrapper_from_file_like_object("test_fixtures/dlip/mets.xml")
    print(wrapper_2.physical_structure)
    wrapper_3 = get_mets_wrapper_from_file_like_object("test_fixtures/wc-goobi/b29356350.xml")
    print(wrapper_3.physical_structure)
    wrapper_4 = get_mets_wrapper_from_file_like_object("test_fixtures/wc-archivematica/METS.299eb16f-1e62-4bf6-b259-c82146153711.xml")
    print(wrapper_4.physical_structure)
