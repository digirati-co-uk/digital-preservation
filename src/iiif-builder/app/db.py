from datetime import datetime

from gino import Gino

db = Gino()

class ArchivalGroupActivity(db.Model):
    """
    A row is created for every Activity Stream event read by the system
    """
    __tablename__ = "archival_group_activity"

    id = db.Column(db.Integer(), primary_key=True) # TODO - how do you make this auto-increment?
    activity_end_time = db.Column(db.DateTime(timezone=False), nullable=True)
    archival_group_uri = db.Column(db.String, nullable=False)
    activity_type = db.Column(db.String, nullable=False) # Create, Update etc
    catalogue_api_uri = db.Column(db.String, nullable=True)
    public_manifest_uri = db.Column(db.String, nullable=True) # may not be for IIIF use
    api_manifest_uri = db.Column(db.String, nullable=True) # may not be for IIIF use
    started = db.Column(db.DateTime(timezone=False), nullable=True)
    finished = db.Column(db.DateTime(timezone=False), nullable=True)
    error_message = db.Column(db.String, nullable=True)

    @staticmethod
    async def get_latest_end_time() -> datetime.datetime:
        sql = "SELECT max(activity_end_time) FROM archival_group_activity"
        result = await db.one(db.text(sql))
        return result