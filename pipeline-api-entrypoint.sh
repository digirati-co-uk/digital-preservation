#!/bin/bash
# Starts clamd (and freshclam) inside this same container, then drops privileges to the app's
# non-root user for the actual .NET process. This container must start as root (see Dockerfile.
# PipelineApi - the trailing "USER $APP_UID" was removed) because clamd needs root to create its
# runtime directories and bind its socket; the .NET app itself still ends up running as $APP_UID.
set -euo pipefail

mkdir -p /var/run/clamav /var/log/clamav
chown clamav:clamav /var/run/clamav /var/log/clamav

# The image's build-time `RUN freshclam` (Dockerfile.PipelineApi) only captures a point-in-time
# snapshot that goes stale after the image is built/deployed - pull the latest before clamd starts.
freshclam || echo "entrypoint: initial freshclam run failed, continuing with the signatures baked into the image"

# Long-running daemon for the lifetime of the container.
su -s /bin/sh clamav -c "clamd" &

# Keep signatures current for the life of the container (freshclam's own Checks setting in
# freshclam.conf controls how often it re-polls).
su -s /bin/sh clamav -c "freshclam -d" &

echo "entrypoint: waiting for clamd socket..."
for i in $(seq 1 120); do
    if [ -S /var/run/clamav/clamd.sock ]; then
        echo "entrypoint: clamd socket ready after ${i}s"
        break
    fi
    sleep 1
done

if [ ! -S /var/run/clamav/clamd.sock ]; then
    echo "entrypoint: clamd socket never appeared after 120s - starting the app anyway, clamscan-shim.sh will fall back to a local scan" >&2
fi

exec setpriv --reuid="$APP_UID" --regid="$APP_UID" --init-groups dotnet Pipeline.API.dll "$@"
