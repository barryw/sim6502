#!/usr/bin/env bash
#
# Run example/ultimate.suite against both the simulated and the real Ultimate 64
# and require identical results. This is the check u64sim exists to satisfy.
#
# Requires a physical machine. Never run in CI.
#
# Before the first run, provision the fixtures onto the machine's stick over FTP
# (the REST API has no arbitrary file-write endpoint):
#
#   curl --ftp-create-dirs -T sim6502tests/Fixtures/usb0/data/hello.txt \
#        ftp://$U64_HOST/USB1/data/hello.txt
#   curl -T sim6502tests/Fixtures/usb0/readme.txt \
#        ftp://$U64_HOST/USB1/readme.txt
#
set -euo pipefail

if [ -z "${U64_HOST:-}" ]; then
    echo "U64_HOST is not set. Usage: make differential U64_HOST=192.168.1.62" >&2
    exit 2
fi

MOUNT="${U64_MOUNT:-USB1}"
SUITE="${U64_SUITE:-example/ultimate.suite}"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

echo "==> Checking the Ultimate is reachable and idle at $U64_HOST"
# $DF1B-$DF1C only (bus ID + control/status) -- NOT a span into $DF1E/$DF1F,
# which are FIFO ports. Reading them here would pop a reply meant for the
# actual run. See IU64Connection.cs:13-16.
if ! IDLE=$(curl -sS -f --max-time 8 \
    "http://$U64_HOST/v1/machine:readmem?address=df1b&length=2" | xxd -p); then
    echo "Could not reach $U64_HOST — check the IP and that the machine is powered on." >&2
    exit 1
fi
echo "    \$DF1B-\$DF1C = $IDLE"
case "$IDLE" in
    ??00*) ;;
    *) echo "    UCI is not idle (\$DF1C != 00). Power-cycle the machine and retry." >&2
       exit 1 ;;
esac

# u64sim's default mount is /Usb0 (example/ultimate.suite hardcodes that
# path); a real stick usually enumerates as /USB1. Substitute the configured
# mount into a single temp copy and run BOTH backends against that copy, so
# chdir/open/read/close are actually exercised on both sides instead of both
# failing identically against a mount neither one serves -- which would
# otherwise print IDENTICAL without the DOS commands ever having been
# compared. (Relative paths don't work here: the DOS working directory is
# sticky across tests, so a later `cd data` would still depend on the earlier
# absolute chdir having landed somewhere real.)
sed "s|/Usb0/|/$MOUNT/|g" "$SUITE" > "$OUT/suite.txt"

# control-reu-absent's LOAD_REU reliably wedges real firmware (fw 3.14d) until
# a power cycle -- see GideonZ/1541ultimate#740. Exclude it from BOTH runs so
# the comparison stays symmetric; u64sim users get it by default since this
# flag is only passed here, not baked into the CLI.
EXCLUDE=(--exclude-tag hardware-wedges)

echo "==> Running $SUITE (mount substituted to /$MOUNT) against u64sim"
dotnet run --project sim6502 -- --suitefile "$OUT/suite.txt" \
    --backend u64sim \
    --u64sim-fs-root sim6502tests/Fixtures/usb0 \
    --u64sim-mount "$MOUNT" \
    "${EXCLUDE[@]}" > "$OUT/u64sim.txt" 2>&1 || true

echo "==> Running $SUITE (mount substituted to /$MOUNT) against real hardware at $U64_HOST"
dotnet run --project sim6502 -- --suitefile "$OUT/suite.txt" \
    --backend u64 --u64-host "$U64_HOST" \
    "${EXCLUDE[@]}" > "$OUT/u64.txt" 2>&1 || true

for f in "$OUT/u64sim.txt" "$OUT/u64.txt"; do
    grep -q "suites passed\." "$f" || {
        echo "ERROR: $f never reached a suite summary — the run did not complete:" >&2
        cat "$f" >&2
        exit 1
    }
done

echo "==> Comparing"
# Every log line carries a timestamp, and u64sim's SimulatorBackend logs a
# copyright/URL banner that u64 structurally cannot (BackendFactory builds no
# processor for "u64"). Neither difference is a real divergence, so compare
# only the listener's lines -- the sole source of test results -- with the
# timestamp/level/logger columns stripped.
norm() {
    grep 'sim6502.Grammar.SimBaseListener' "$1" \
        | sed -E 's/^[^|]+\| [A-Z]+ +\| [^|]+\| //'
}
norm "$OUT/u64sim.txt" > "$OUT/a.txt"
norm "$OUT/u64.txt"    > "$OUT/b.txt"

if diff -u "$OUT/a.txt" "$OUT/b.txt"; then
    echo "==> IDENTICAL. u64sim matches silicon for $SUITE."
else
    echo "==> DIVERGENCE. Each difference is either a u64sim bug or a firmware bug." >&2
    echo "    Investigate before assuming u64sim is wrong -- see" >&2
    echo "    GideonZ/1541ultimate#740 for a case where the firmware was at fault." >&2
    exit 1
fi
