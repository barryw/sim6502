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
if ! IDLE=$(curl -sS -f --max-time 8 \
    "http://$U64_HOST/v1/machine:readmem?address=df1b&length=5" | xxd -p); then
    echo "Could not reach $U64_HOST — check the IP and that the machine is powered on." >&2
    exit 1
fi
echo "    \$DF1B-\$DF1F = $IDLE"
case "$IDLE" in
    ??00*) ;;
    *) echo "    UCI is not idle (\$DF1C != 00). Power-cycle the machine and retry." >&2
       exit 1 ;;
esac

echo "==> Running $SUITE against u64sim"
dotnet run --project sim6502 -- --suitefile "$SUITE" \
    --backend u64sim \
    --u64sim-fs-root sim6502tests/Fixtures/usb0 \
    --u64sim-mount "$MOUNT" > "$OUT/u64sim.txt" 2>&1 || true

echo "==> Running $SUITE against real hardware at $U64_HOST"
dotnet run --project sim6502 -- --suitefile "$SUITE" \
    --backend u64 --u64-host "$U64_HOST" > "$OUT/u64.txt" 2>&1 || true

for f in "$OUT/u64sim.txt" "$OUT/u64.txt"; do
    grep -q "suites passed\." "$f" || {
        echo "ERROR: $f never reached a suite summary — the run did not complete:" >&2
        cat "$f" >&2
        exit 1
    }
done

echo "==> Comparing"
# Strip the backend banner lines, which legitimately differ.
sed -E 's/^.*(u64sim ready|Connecting to Ultimate).*$//' "$OUT/u64sim.txt" > "$OUT/a.txt"
sed -E 's/^.*(u64sim ready|Connecting to Ultimate).*$//' "$OUT/u64.txt"    > "$OUT/b.txt"

if diff -u "$OUT/a.txt" "$OUT/b.txt"; then
    echo "==> IDENTICAL. u64sim matches silicon for $SUITE."
else
    echo "==> DIVERGENCE. Each difference is either a u64sim bug or a firmware bug." >&2
    echo "    Investigate before assuming u64sim is wrong -- see" >&2
    echo "    GideonZ/1541ultimate#740 for a case where the firmware was at fault." >&2
    exit 1
fi
