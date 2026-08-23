#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Starts a published headless host and asserts on what it actually serves.
#
# Every test in TelemetryDashboard.Tests feeds the code data the test itself
# invented. This does not: it launches the single-file binary that an operator
# would be handed, on the platform in question, and asks it questions over HTTP.
#
# It exists because ARCHITECTURE.md and PROJECT.md both record — accurately —
# that the Linux and macOS packages have valid executable headers and have never
# been executed. A header is not a run. This is the run.
#
# Usage:  smoke-host.sh <published-host-directory> [port]
# ---------------------------------------------------------------------------
set -uo pipefail

HOST_DIR=${1:?usage: smoke-host.sh <published-host-directory> [port]}
PORT=${2:-8099}
# The .exe fallback is not for CI -- it is so this script can be run against a
# win-x64 publish on the development machine, where every assertion below except
# the platform itself can be checked before it is trusted in a pipeline. A CI
# script whose first execution is in CI is one nobody can debug.
BIN="$HOST_DIR/TelemetryDashboard.Host"
[ -f "$BIN" ] || BIN="$HOST_DIR/TelemetryDashboard.Host.exe"
LOG="$HOST_DIR/smoke-host.log"
BASE="http://127.0.0.1:$PORT"

failures=0
pass() { printf '  PASS  %s\n' "$1"; }
fail() { printf '  FAIL  %s\n' "$1"; [ $# -gt 1 ] && printf '        %s\n' "$2"; failures=$((failures + 1)); }

check() {  # check <name> <condition-exit-code> [detail]
    if [ "$2" -eq 0 ]; then pass "$1"; else fail "$1" "${3:-}"; fi
}

echo "=== host smoke test: $(uname -s) $(uname -m) ==="

if [ ! -f "$BIN" ]; then
    echo "  FAIL  the published host does not exist at $BIN"
    ls -la "$HOST_DIR" || true
    exit 1
fi
chmod +x "$BIN"

# --simulate, so the run needs no serial port and every frame it emits is marked
# simulated=true with a SIM: node prefix. What is being tested is the host, not a
# device: whether this binary starts on this OS, opens a listening socket, drives
# the ingest pipeline and serves the console assets that shipped beside it.
"$BIN" --simulate --port "$PORT" > "$LOG" 2>&1 &
HOST_PID=$!

cleanup() {
    if kill -0 "$HOST_PID" 2>/dev/null; then kill -KILL "$HOST_PID" 2>/dev/null || true; fi
}
trap cleanup EXIT

# A single-file self-contained build extracts itself on first launch, so the first
# start is much slower than every later one. Sixty seconds is for that, not for
# the pipeline.
echo "--- waiting for $BASE/api/status ---"
ready=1
for _ in $(seq 1 60); do
    if ! kill -0 "$HOST_PID" 2>/dev/null; then
        echo "  FAIL  the host exited before it began serving. Its output:"
        sed 's/^/        /' "$LOG"
        exit 1
    fi
    if curl -fsS --max-time 2 "$BASE/api/status" -o /tmp/status.json 2>/dev/null; then ready=0; break; fi
    sleep 1
done
check "the host answers /api/status" "$ready" "sixty seconds elapsed with no reply; see the log artifact"
[ "$ready" -eq 0 ] || { sed 's/^/        /' "$LOG"; exit 1; }

# Give the simulator a moment to actually produce samples. Serving is one claim;
# carrying data is the one worth making.
sleep 5
curl -fsS --max-time 5 "$BASE/api/status" -o /tmp/status.json

python3 - /tmp/status.json <<'PY'
import json, sys

status = json.load(open(sys.argv[1]))
bad = []

def want(name, ok, detail):
    print(f"  {'PASS' if ok else 'FAIL'}  {name}")
    if not ok:
        print(f"        {detail}")
        bad.append(name)

channels = status.get("seriesChannels", 0)
accepted = status.get("seriesSamplesAccepted", 0)
endpoints = status.get("endpoints") or []

want("the ingest pipeline produced channels", channels > 0,
     f"seriesChannels={channels} -- the host is serving, and has nothing to serve")
want("samples were accepted, not merely offered", accepted > 0,
     f"seriesSamplesAccepted={accepted}")
want("every advertised endpoint is declared", len(endpoints) == 13,
     f"status advertises {len(endpoints)}: {endpoints}")

# Refused samples are a real number rather than an absent key: this project's rule
# is that a count nobody kept is not the same fact as a count that came out zero.
want("the refusal counter is reported", "seriesSamplesRefused" in status,
     "seriesSamplesRefused missing -- drops would be invisible")

print(f"        channels={channels} samplesAccepted={accepted} endpoints={len(endpoints)}")
sys.exit(1 if bad else 0)
PY
check "the status payload is what a console can read" "$?"

# Each advertised GET endpoint actually answers. /ws and /stream are long-lived
# and are checked separately below; /api/control is a POST.
echo "--- endpoints ---"
for path in /api/series /api/spectrum /api/aligned /api/computed /api/limits /api/history /api/incident; do
    code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$BASE$path")
    # 400 is a legitimate answer to a query with no parameters; 404 and 500 are not.
    case "$code" in
        200|400) pass "$path answers ($code)" ;;
        *)       fail "$path answers ($code)" "expected 200 or 400" ;;
    esac
done

# The console assets. This guards a defect that actually shipped: the host csproj
# globbed *.html but named telemetry-client.js by hand, so dab_psfb_console.html
# reached the output directory and power_flow.js did not. The host served the page
# and answered 404 for the diagram it loads, and no harness noticed -- one reads
# the file off disk, the other stubs the module.
echo "--- assets shipped beside the binary ---"
for asset in / /stream_client.html /power_flow.js /telemetry-client.js; do
    code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$BASE$asset")
    check "$asset is served ($code)" "$([ "$code" = 200 ] && echo 0 || echo 1)" "expected 200"
done

# SSE. curl is stopped by --max-time, which is an error exit by design, so the
# check is on what arrived rather than on curl's status.
echo "--- live stream ---"
curl -sN --max-time 8 "$BASE/stream" -o /tmp/stream.txt 2>/dev/null || true
frames=$(grep -c '^data:' /tmp/stream.txt 2>/dev/null || echo 0)
check "the SSE stream delivered frames ($frames)" "$([ "$frames" -gt 0 ] && echo 0 || echo 1)" \
      "no data: lines in eight seconds"
if [ "$frames" -gt 0 ]; then
    # --simulate must mark what it produces. A synthetic reading that reaches a
    # chart unlabelled is the failure this product's provenance rules exist for.
    check "simulated frames say they are simulated" \
          "$(grep -q '"simulated":true' /tmp/stream.txt && echo 0 || echo 1)" \
          "frames carry no simulated=true marker"
fi

# Shutdown. ShutdownCoordinator turns SIGINT into one cancellation token and holds
# the process open until the recorder has drained, so a clean stop is part of what
# is being checked -- a host that has to be killed truncates the tail of whatever
# it was writing.
echo "--- shutdown ---"
kill -INT "$HOST_PID" 2>/dev/null || true
stopped=1
for _ in $(seq 1 15); do
    if ! kill -0 "$HOST_PID" 2>/dev/null; then stopped=0; break; fi
    sleep 1
done

case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
        # Git Bash cannot deliver a POSIX SIGINT to a native Windows process, so a
        # host that stays up here says nothing about the host. Reported as unmeasured
        # rather than failed: this script's whole purpose is checking what actually
        # happened, and a red mark for a signal that was never delivered would be the
        # same unfounded claim in the opposite direction. The CI platforms are the
        # ones this check is for; on Windows, run the desktop shell's own tests.
        if [ "$stopped" -eq 0 ]; then
            pass "the host stopped on SIGINT within 15s"
        else
            printf '  NOTE  shutdown not checked: %s cannot signal a native Windows process\n' "$(uname -s)"
        fi
        ;;
    *)
        check "the host stopped on SIGINT within 15s" "$stopped" "still running; it was killed"
        ;;
esac

if [ "$stopped" -eq 0 ]; then
    wait "$HOST_PID"; code=$?
    # 130 means the runtime took the signal's default action instead of the handler
    # completing. Reported rather than asserted: it is worth knowing and has never
    # been measured on these platforms, and failing the build on an expectation
    # nobody has verified would make this script's first run a lie either way.
    if [ "$code" -eq 0 ]; then pass "it exited 0"; else printf '  NOTE  it exited %s (0 means the drain completed)\n' "$code"; fi
fi

echo "--- host output ---"
sed 's/^/        /' "$LOG"

echo
if [ "$failures" -eq 0 ]; then
    echo "=== host smoke test passed on $(uname -s) $(uname -m) ==="
    exit 0
fi
echo "=== host smoke test: $failures check(s) failed on $(uname -s) $(uname -m) ==="
exit 1
