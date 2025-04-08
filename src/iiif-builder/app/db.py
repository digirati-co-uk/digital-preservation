from datetime import datetime, timezone
import psycopg
from logzero import logger

from app import settings

class ArchivalGroupActivity:
    """
    A row is created for every Activity Stream event read by the system
    """
    def __init__(self,
                 id_:int=0,
                 activity_end_time:datetime=None,
                 archival_group_uri:str=None,
                 activity_type:str=None,
                 id_service_pid:str=None,
                 catalogue_api_uri:str=None,
                 public_manifest_uri:str=None,
                 internal_public_manifest_uri:str=None,
                 internal_api_manifest_uri:str=None,
                 started:datetime=None,
                 finished:datetime=None,
                 error_message:str=None
                 ):
        self.id_ = id_
        self.activity_end_time = activity_end_time
        self.archival_group_uri = archival_group_uri
        self.activity_type = activity_type
        self.id_service_pid = id_service_pid
        self.catalogue_api_uri = catalogue_api_uri
        self.public_manifest_uri = public_manifest_uri
        self.internal_public_manifest_uri = internal_public_manifest_uri
        self.internal_api_manifest_uri = internal_api_manifest_uri
        self.started = started
        self.finished = finished
        self.error_message = error_message


    @staticmethod
    def get_latest_end_time() -> datetime:
        if settings.ACTIVITY_CUTOFF_DATE is not None:
            if settings.ACTIVITY_CUTOFF_DATE.lower() == "now":
                logger.info("Found 'now' as activity cutoff date")
                return datetime.now(tz=timezone.utc)
            try:
                logger.info(f"Trying to parse {settings.ACTIVITY_CUTOFF_DATE} for activity cutoff date")
                cutoff = datetime.fromisoformat(settings.ACTIVITY_CUTOFF_DATE)
                return cutoff
            except ValueError:
                logger.error(f"Unable to parse {settings.ACTIVITY_CUTOFF_DATE} for activity cutoff date, returning current datetime instead")
                return datetime.now(tz=timezone.utc)

        with psycopg.connect(settings.POSTGRES_CONNECTION) as conn:
            with conn.cursor() as cur:
                next_res = cur.execute("SELECT max(activity_end_time) FROM archival_group_activity").fetchone()
                if next_res is not None and next_res[0] is not None:
                    return next_res[0]

        return datetime(2025, 4, 8, tzinfo=timezone.utc)


    @classmethod
    def new_activity(cls, activity_end_time_date, archival_group_uri, activity_type)-> 'ArchivalGroupActivity':
        with psycopg.connect(settings.POSTGRES_CONNECTION) as conn:
            with conn.cursor() as cur:
                sql = ("INSERT INTO archival_group_activity "
                       "(activity_end_time, archival_group_uri, activity_type, started) "
                       "VALUES (%s, %s, %s, %s) "
                       "RETURNING id")
                values = (activity_end_time_date, archival_group_uri, activity_type, datetime.now(tz=timezone.utc))
                new_id = cur.execute(sql, values).fetchone()[0]

        if new_id != -1:
            return ArchivalGroupActivity.get_from_id(new_id)


    @staticmethod
    def get_from_id(id_:int)-> 'ArchivalGroupActivity | None':
        with psycopg.connect(settings.POSTGRES_CONNECTION) as conn:
            with conn.cursor() as cur:
                sql = ("SELECT id, activity_end_time, archival_group_uri, activity_type, "
                       "id_service_pid, catalogue_api_uri, public_manifest_uri, "
                       "internal_public_manifest_uri, internal_api_manifest_uri, "
                       "started, finished, error_message "
                       "FROM archival_group_activity WHERE id = %s")
                result = cur.execute(sql, [id_]).fetchone()
                if result is None:
                    return None
                row = result
                return ArchivalGroupActivity(
                    id_=row[0],
                    activity_end_time=row[1],
                    archival_group_uri=row[2],
                    activity_type=row[3],
                    id_service_pid=row[4],
                    catalogue_api_uri=row[5],
                    public_manifest_uri=row[6],
                    internal_public_manifest_uri=row[7],
                    internal_api_manifest_uri=row[8],
                    started=row[9],
                    finished=row[10],
                    error_message=row[11]
                )


    def save(self):
        with psycopg.connect(settings.POSTGRES_CONNECTION) as conn:
            with conn.cursor() as cur:
                sql = ("UPDATE archival_group_activity SET  "
                       "id_service_pid=%s, catalogue_api_uri=%s, public_manifest_uri=%s, "
                       "internal_public_manifest_uri=%s, internal_api_manifest_uri=%s, "
                       "finished=%s, error_message=%s "
                       "WHERE id = %s")
                values = (
                    self.id_service_pid,
                    self.catalogue_api_uri,
                    self.public_manifest_uri,
                    self.internal_public_manifest_uri,
                    self.internal_api_manifest_uri,
                    self.finished,
                    self.error_message,
                    self.id_
                )
                cur.execute(sql, values)



# create table archival_group_activity
# (
#     id                           serial
#         primary key,
#     activity_end_time            timestamp with time zone not null,
#     archival_group_uri           text                     not null,
#     activity_type                text                     not null,
#     id_service_pid               text,
#     catalogue_api_uri            text,
#     public_manifest_uri          text,
#     internal_public_manifest_uri text,
#     internal_api_manifest_uri    text,
#     started                      timestamp with time zone not null,
#     finished                     timestamp with time zone,
#     error_message                text
# );
#
# alter table archival_group_activity
#     owner to postgres;
