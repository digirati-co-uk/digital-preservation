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

# Long-running daemon for the lifetime of the container. clamd has no built-in restart-on-crash
# behaviour, and if it dies (e.g. killed under host memory pressure - see LPII-135) clamscan-shim.sh
# doesn't fail the scan, it just logs "Could not connect to clamd ... Connection refused" and reports
# "Infected files: 0" - a false clean result, not an actual scan. Since this entrypoint script exec's
# into dotnet as its last step, there's no shell left to notice or restart a background `&` job, so
# supervise it in its own loop (backgrounded before the exec) instead of a bare fire-and-forget start.
supervise_clamd() {
    while true; do
        # `|| true`: this script runs under `set -e`, and a bare failing command here (rather than
        # one in an if/while-condition or already part of a `||`) would kill this loop on clamd's
        # first crash instead of restarting it.
        su -s /bin/sh clamav -c "clamd" || true
        echo "entrypoint: clamd exited - restarting in 2s" >&2
        sleep 2
    done
}
supervise_clamd &

# Keep signatures current for the life of the container (freshclam's own Checks setting in
# freshclam.conf controls how often it re-polls). Supervised for the same reason as clamd above.
supervise_freshclam() {
    while true; do
        su -s /bin/sh clamav -c "freshclam -d" || true
        echo "entrypoint: freshclam exited - restarting in 2s" >&2
        sleep 2
    done
}
supervise_freshclam &

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

# setpriv changes the process's UID/GID but does NOT reset HOME the way su does - without this,
# the dotnet process (and everything it spawns, including Brunnhilde/Siegfried/sf) would inherit
# HOME=/root from this root shell despite running as a non-root UID, causing sf to look for its
# signature file under /root instead of this user's actual home and fail with "Siegfried is not
# installed or available on PATH".
APP_HOME=$(getent passwd "$APP_UID" | cut -d: -f6)
exec env HOME="$APP_HOME" setpriv --reuid="$APP_UID" --regid="$APP_UID" --init-groups dotnet Pipeline.API.dll "$@"
