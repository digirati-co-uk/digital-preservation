import os

POSTGRES_CONNECTION = os.environ.get('POSTGRES_CONNECTION')
ACTIVITY_STREAM_READ_INTERVAL = float(os.environ.get('ACTIVITY_STREAM_READ_INTERVAL', '60.0'))
PRESERVATION_ACTIVITY_STREAM = os.environ.get('PRESERVATION_ACTIVITY_STREAM')

IDENTITY_SERVICE_BASE_URL = os.environ.get('IDENTITY_SERVICE_BASE_URL', 'https://id.library.leeds.ac.uk/api/v1')
IDENTITY_SERVICE_API_HEADER = "Authorization"
IDENTITY_SERVICE_API_KEY = os.environ.get('IDENTITY_SERVICE_API_KEY')

REWRITTEN_PUBLIC_IIIF_PRESENTATION_PREFIX = os.environ.get('REWRITTEN_PUBLIC_IIIF_PRESENTATION_PREFIX', 'https://iiif.leeds.ac.uk/presentation/')
IIIF_CS_CUSTOMER_ID = os.environ.get('IIIF_CS_CUSTOMER_ID', 2)
IIIF_CS_ASSET_SPACE_ID = os.environ.get('IIIF_CS_ASSET_SPACE_ID', 5)
IIIF_CS_PRESENTATION_HOST = os.environ.get('IIIF_CS_PRESENTATION_HOST', 'https://iiif-cs.library.leeds.ac.uk')
IIIF_CS_BASIC_CREDENTIALS = os.environ.get('IIIF_CS_BASIC_CREDENTIALS')

USE_MVP_CATALOGUE_API = os.environ.get('USE_MVP_CATALOGUE_API', True)
MVP_CATALOGUE_API_PREFIX = os.environ.get('MVP_CATALOGUE_API_PREFIX', 'https://explore.library.leeds.ac.uk/imu/utilities/getIIIFData.php?pid=')
MVP_CATALOGUE_API_KEY_HEADER = os.environ.get('MVP_CATALOGUE_API_HEADER', 'X-API-KEY')
MVP_CATALOGUE_API_KEY_VALUE = os.environ.get('MVP_CATALOGUE_API_KEY_VALUE')
# This value is on the wiki page
# https://dev.azure.com/universityofleeds/Library/_wiki/wikis/Library.wiki/4864/Present-IIIF(new)-manifests-to-Website

