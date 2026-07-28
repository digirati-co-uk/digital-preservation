#!/bin/bash
# Brunnhilde invokes the literal "clamscan" command with no way to configure it - this shim
# shadows the real clamscan binary (moved aside to clamscan.real during the image build, see
# Dockerfile.PipelineApi) and redirects to the clamd daemon running on the EC2 host, reached via
# the Unix socket bind-mounted into this container at /var/run/clamav/clamd.sock. This avoids
# clamscan reloading the entire virus signature database from disk on every single pipeline job
# run.
#
# clamdscan does not accept --max-scansize/--max-filesize per invocation the way clamscan does -
# those limits are configured as MaxScanSize/MaxFileSize in the host's clamd.conf instead, so
# strip them here rather than letting clamdscan reject the call outright.

set -euo pipefail

SOCKET="${CLAMD_SOCKET:-/var/run/clamav/clamd.sock}"

if [ ! -S "$SOCKET" ]; then
    echo "clamscan shim: $SOCKET not found, falling back to a local clamscan scan" >&2
    exec /usr/bin/clamscan.real "$@"
fi

args=()
for arg in "$@"; do
    case "$arg" in
        --max-scansize=*|--max-filesize=*)
            continue
            ;;
        *)
            args+=("$arg")
            ;;
    esac
done

# clamdscan has no --socket flag - it reads LocalSocket from a config file via --config-file,
# so point it at the real clamd.conf (which already has LocalSocket set to $SOCKET) instead.
#
# -m/--multiscan tells clamd to scan multiple files in the directory concurrently across
# MaxThreads worker threads, instead of one file at a time. Measured on a real 2796-file/37GB
# deposit: 21m21s single-threaded vs 6m53s with multiscan (~3.1x on 4 cores) - the dominant cost
# for a large deposit is scan throughput across many files, which multiscan directly parallelizes.
exec clamdscan --config-file=/etc/clamav/clamd.conf --multiscan "${args[@]}"
