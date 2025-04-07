import asyncio

from app import iiif_builder


if __name__ == "__main__":
    asyncio.run(iiif_builder.read_stream())