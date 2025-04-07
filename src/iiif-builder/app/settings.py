import os
from dotenv import load_dotenv

load_dotenv()  # take environment variables from .env file; to then be superseded by the below

# IIIF-Builder's dedicated DB for recording activity
POSTGRES_CONNECTION = os.environ.get('POSTGRES_CONNECTION')
ACTIVITY_STREAM_READ_INTERVAL = float(os.environ.get('ACTIVITY_STREAM_READ_INTERVAL', '60.0'))
PRESERVATION_ACTIVITY_STREAM = os.environ.get('PRESERVATION_ACTIVITY_STREAM')
ACTIVITY_CUTOFF_DATE = os.environ.get('ACTIVITY_CUTOFF_DATE', "NOW") # or a parseable timestamp, or None.  Example '2011-11-04T00:05:23Z'

# The header that iiif-builder passes to Preservation API as X-Client-Identity
PRESERVATION_CLIENT_IDENTITY_HEADER = os.environ.get('PRESERVATION_CLIENT_IDENTITY_HEADER', "X-Client-Identity")
IIIF_BUILDER_IDENTITY = os.environ.get('IIIF_BUILDER_IDENTITY', "iiif-builder")
PRESERVATION_COLLECTIONS_CONTAINER_ALIASES = os.environ.get('PRESERVATION_COLLECTIONS_CONTAINER_ALIASES', None)
PRESERVATION_COLLECTIONS_HOST_ALIASES = os.environ.get('PRESERVATION_COLLECTIONS_HOST_ALIASES', None)
ARCHIVAL_GROUP_PREFIXES_TO_PROCESS = os.environ.get('ARCHIVAL_GROUP_PREFIXES_TO_PROCESS', 'cc-test,cc,iiifb/demo/deep')

# OAuth2 (MS flavoured) settings for calling Preservation API
PRESERVATION_CLIENT_ID = os.environ.get('PRESERVATION_CLIENT_ID')
PRESERVATION_CLIENT_SECRET = os.environ.get('PRESERVATION_CLIENT_SECRET')
PRESERVATION_TENANT_ID = os.environ.get('PRESERVATION_TENANT_ID')
PRESERVATION_SCOPE = f"api://{PRESERVATION_CLIENT_ID}/.default"
PRESERVATION_AUTHORITY_URL = f"https://login.microsoftonline.com/{PRESERVATION_TENANT_ID}" # /oauth2/token"

# Details for calling Leeds Identity Service
IDENTITY_SERVICE_BASE_URL = os.environ.get('IDENTITY_SERVICE_BASE_URL', 'https://dev-id.library.leeds.ac.uk/api/v1')
IDENTITY_SERVICE_API_HEADER = os.environ.get('IDENTITY_SERVICE_API_HEADER', "X-API-KEY")
IDENTITY_SERVICE_API_KEY = os.environ.get('IDENTITY_SERVICE_API_KEY')

# IIIF Cloud Services configuration
REWRITTEN_PUBLIC_IIIF_PRESENTATION_PREFIX = os.environ.get('REWRITTEN_PUBLIC_IIIF_PRESENTATION_PREFIX', 'https://iiif.leeds.ac.uk/presentation/')
IIIF_CS_CUSTOMER_ID = os.environ.get('IIIF_CS_CUSTOMER_ID', 2)
IIIF_CS_ASSET_SPACE_ID = os.environ.get('IIIF_CS_ASSET_SPACE_ID', 5)
IIIF_CS_PRESENTATION_HOST = os.environ.get('IIIF_CS_PRESENTATION_HOST', 'https://dev-iiif.leeds.ac.uk/presentation/')
IIIF_CS_BASIC_CREDENTIALS = os.environ.get('IIIF_CS_BASIC_CREDENTIALS')

# Catalogue API details (MVP version)
CONSTRUCT_CATALOGUE_API_URI = os.environ.get('CONSTRUCT_CATALOGUE_API_URI', False)
MVP_CATALOGUE_API_PREFIX = os.environ.get('MVP_CATALOGUE_API_PREFIX', 'https://explore.library.leeds.ac.uk/imu/utilities/getIIIFData.php?pid=')
MVP_CATALOGUE_API_KEY_HEADER = os.environ.get('MVP_CATALOGUE_API_HEADER', 'X-API-KEY')
MVP_CATALOGUE_API_KEY_VALUE = os.environ.get('MVP_CATALOGUE_API_KEY_VALUE')
# This value is on the wiki page
# https://dev.azure.com/universityofleeds/Library/_wiki/wikis/Library.wiki/4864/Present-IIIF(new)-manifests-to-Website

