FROM python:3.12-slim-bookworm

RUN apt-get update -y && apt-get install -y libpq5

COPY requirements.txt /opt/app/requirements.txt
WORKDIR /opt/app
RUN pip install --no-cache-dir -r requirements.txt

COPY . /opt/app

CMD ["python3", "/opt/app/iiif_builder.py"]