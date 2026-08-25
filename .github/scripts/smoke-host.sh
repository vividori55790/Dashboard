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
# Scratch files live beside the host rather than in /tmp: this script is bash and the
# blocks inside it are python, and on a Windows checkout those two do not agree on what
# /tmp resolves to. The first version relied on them agreeing, and the endpoint loop
# below silently iterated an empty list and passed.
STATUS="$HOST_DIR/status.json"
ENDPOINTS="$HOST_DIR/endpoints.txt"
STREAM="$HOST_DIR/stream.txt"
BASE="http://127.0.0.1:$PORT"

failures=0
pass() { printf '  PASS  %s\n' "$1"; }

# A failure is also emitted as a workflow annotation, because the step log needs a
# token to read and the annotations do not. The first three failed runs of this
# workflow could only say "Process completed with exit code 1" to anyone outside
# them, which for a public repository means the check reports to nobody.
annotate() {
    [ -n "${GITHUB_ACTIONS:-}" ] || return 0
    printf '::error title=Host smoke (%s): %s::%s\n' "$(uname -s)" "$1" \
        "$(printf '%s' "${2:-no detail}" | sed 's/%/%25/g' | tr '\r\n' '  ')"
}

fail() {
    printf '  FAIL  %s\n' "$1"
    [ $# -gt 1 ] && printf '        %s\n' "$2"
    annotate "$1" "${2:-}"
    failures=$((failures + 1))
}

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
# Job control on, before the launch. Without it a non-interactive shell starts an
# asynchronous command with SIGINT and SIGQUIT set to ignored -- POSIX requires that,
# so the shell's own Ctrl-C does not fell its background children. The consequence
# here is that `kill -INT` would be dropped by the kernel before the process ever saw
# it, and the run would report a host that refuses to stop when what actually happened
# is that nothing asked it to. With job control the child gets its own process group
# and the default disposition.
set -m

"$BIN" --simulate --port "$PORT" > "$LOG" 2>&1 &
HOST_PID=$!
set +m

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
    if curl -fsS --max-time 2 "$BASE/api/status" -o "$STATUS" 2>/dev/null; then ready=0; break; fi
    sleep 1
done
check "the host answers /api/status" "$ready" "sixty seconds elapsed with no reply; see the log artifact"
[ "$ready" -eq 0 ] || { sed 's/^/        /' "$LOG"; exit 1; }

# Give the simulator a moment to actually produce samples. Serving is one claim;
# carrying data is the one worth making.
sleep 5
curl -fsS --max-time 5 "$BASE/api/status" -o "$STATUS"

python3 - "$STATUS" "$ENDPOINTS" <<'PY'
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
want("the endpoint list is advertised at all", len(endpoints) >= 13,
     f"status advertises {len(endpoints)}: {endpoints}")

# Written out so the endpoint loop below asks the product what it serves instead of
# carrying a second copy of the list. The first version hardcoded 13, and adding
# /api/inputs broke it -- correctly, but for the wrong reason: nothing was wrong with
# the host, only with the harness's memory of it.
open(sys.argv[2], "w").write("\n".join(endpoints))

# Refused samples are a real number rather than an absent key: this project's rule
# is that a count nobody kept is not the same fact as a count that came out zero.
want("the refusal counter is reported", "seriesSamplesRefused" in status,
     "seriesSamplesRefused missing -- drops would be invisible")

print(f"        channels={channels} samplesAccepted={accepted} endpoints={len(endpoints)}")
sys.exit(1 if bad else 0)
PY
check "the status payload is what a console can read" "$?"

# /ws and /stream are long-lived and are checked on their own below; /api/control is a
# POST. Everything else the host advertises has to answer, and the list comes from the
# host rather than from here.
queryable=$(grep -vE '^(/ws|/stream|/api/status|/api/control)$' "$ENDPOINTS" 2>/dev/null || true)
# A loop over an empty list passes without checking anything, which is the one outcome
# a check must never have. This is the guard that makes the loop below mean something.
check "the advertised endpoint list was captured" \
      "$([ -n "$queryable" ] && echo 0 || echo 1)" \
      "no endpoints to query -- the status payload was not captured, so nothing below ran"

for path in $queryable; do
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
curl -sN --max-time 8 "$BASE/stream" -o "$STREAM" 2>/dev/null || true
frames=$(grep -c '^data:' "$STREAM" 2>/dev/null || echo 0)
check "the SSE stream delivered frames ($frames)" "$([ "$frames" -gt 0 ] && echo 0 || echo 1)" \
      "no data: lines in eight seconds"
if [ "$frames" -gt 0 ]; then
    # --simulate must mark what it produces. A synthetic reading that reaches a
    # chart unlabelled is the failure this product's provenance rules exist for.
    check "simulated frames say they are simulated" \
          "$(grep -q '"simulated":true' "$STREAM" && echo 0 || echo 1)" \
          "frames carry no simulated=true marker"
fi

# WebSocket. This is the single most-cited unknown in the documentation: "Not yet
# verified: HttpListener's WebSocket path executing on a real Linux or macOS
# machine." Checking SSE does not answer it -- telemetry-client.js falls back to SSE
# precisely when the WebSocket handshake fails, so a console that works proves
# nothing about /ws, and the fallback is what would hide the failure.
#
# Done over a raw socket rather than with a client library, because the runners have
# no such library and a smoke test should not need one installed to answer a
# question about the product.
echo "--- websocket ---"
ws_report=$(python3 - "$PORT" <<'PY' 2>&1
import json, socket, sys

port = int(sys.argv[1])

# The key/accept pair published in RFC 6455 section 1.3, used as a fixed vector
# rather than recomputing the digest here. Recomputing means hardcoding the magic
# GUID, and a constant typed from memory is a check that can fail for a reason that
# has nothing to do with the server -- which is exactly what happened on the first
# attempt at this: a wrong GUID reported a correct handshake as broken. The RFC's
# own published answer cannot be mistyped in a way that still matches.
#
# A fixed key is fine here. Its randomness defends real clients against cache
# poisoning by intermediaries; there are none on a loopback smoke test.
key = "dGhlIHNhbXBsZSBub25jZQ=="
expected = "s3pPLMBiTxaQ9kYGzzhZRbK+xOo="

sock = socket.create_connection(("127.0.0.1", port), timeout=10)
sock.settimeout(10)
sock.sendall((
    f"GET /ws HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n"
    f"Upgrade: websocket\r\nConnection: Upgrade\r\n"
    f"Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode())

buf = b""
while b"\r\n\r\n" not in buf:
    chunk = sock.recv(4096)
    if not chunk:
        print("  FAIL  the server closed the connection during the handshake")
        sys.exit(1)
    buf += chunk

head, _, rest = buf.partition(b"\r\n\r\n")
head = head.decode("latin-1")
status = head.split("\r\n", 1)[0]

if "101" not in status:
    print(f"  FAIL  /ws did not switch protocols: {status}")
    print("        HttpListener's WebSocket path is the documented unknown on this platform")
    sys.exit(1)
print(f"  PASS  /ws switched protocols ({status.strip()})")

accept = next((line.split(":", 1)[1].strip() for line in head.split("\r\n")
               if line.lower().startswith("sec-websocket-accept:")), None)
if accept != expected:
    print(f"  FAIL  Sec-WebSocket-Accept is wrong: {accept!r} != {expected!r}")
    sys.exit(1)
print("  PASS  the handshake accept token is correct")

# One server frame. Server-to-client frames are never masked, so the payload starts
# straight after the length.
while len(rest) < 2:
    rest += sock.recv(4096)
opcode = rest[0] & 0x0F
length = rest[1] & 0x7F
offset = 2
if length == 126:
    while len(rest) < 4: rest += sock.recv(4096)
    length = int.from_bytes(rest[2:4], "big"); offset = 4
elif length == 127:
    while len(rest) < 10: rest += sock.recv(4096)
    length = int.from_bytes(rest[2:10], "big"); offset = 10

while len(rest) < offset + length:
    chunk = sock.recv(4096)
    if not chunk: break
    rest += chunk
payload = rest[offset:offset + length]
sock.close()

if opcode != 0x1:
    print(f"  FAIL  the first frame is opcode {opcode:#x}, not text")
    sys.exit(1)

try:
    frame = json.loads(payload.decode("utf-8"))
except Exception as error:
    print(f"  FAIL  the frame is not JSON a console could read: {error}")
    print(f"        {payload[:200]!r}")
    sys.exit(1)

print(f"  PASS  a text frame arrived and parses as JSON ({length} bytes)")
print(f"        keys: {sorted(frame)[:8] if isinstance(frame, dict) else type(frame).__name__}")
PY
)
ws_status=$?
printf '%s\n' "$ws_report"
check "the WebSocket path works on this platform" "$ws_status" \
      "documented as unverified on Linux and macOS -- this is the check that answers it. $(printf '%s' "$ws_report" | grep -E '^\s*(FAIL|Traceback|[A-Za-z]*Error)' | head -2 | tr '\n' ' ')"

# Shutdown. ShutdownCoordinator turns SIGINT into one cancellation token and holds
# the process open until the recorder has drained, so a clean stop is part of what
# is being checked -- a host that has to be killed truncates the tail of whatever
# it was writing.
echo "--- shutdown ---"

# Two signals, reported separately, because they are two different deployments.
# SIGINT is an operator at a terminal pressing Ctrl-C; SIGTERM is systemd, docker stop
# or a service manager, and it is the one a plant host actually receives. The
# coordinator claims both -- Console.CancelKeyPress and ProcessExit -- and neither had
# ever been measured, because the only harness for it runs on Windows where Git Bash
# cannot deliver a POSIX signal at all.
#
# Which one worked is reported rather than assumed. A host that ignores Ctrl-C but
# stops cleanly under a service manager is a different fact from one that ignores
# both, and only the second is a reason to fail this run.
waitfor() {
    for _ in $(seq 1 "$2"); do
        if ! kill -0 "$HOST_PID" 2>/dev/null; then return 0; fi
        sleep 1
    done
    return 1
}

stopped_by=""
kill -INT "$HOST_PID" 2>/dev/null || true
if waitfor "$HOST_PID" 10; then
    stopped_by="SIGINT"
else
    printf '  NOTE  still running 10s after SIGINT; trying SIGTERM\n'
    kill -TERM "$HOST_PID" 2>/dev/null || true
    if waitfor "$HOST_PID" 10; then stopped_by="SIGTERM"; fi
fi

stopped=1
[ -n "$stopped_by" ] && stopped=0

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
        check "the host stops on a termination signal" "$stopped" \
              "neither SIGINT nor SIGTERM ended it within ten seconds each; it was killed, which is \
what an operator's service manager would end up doing and what truncates a recording's tail"
        [ -n "$stopped_by" ] && printf '  NOTE  it was %s that stopped it\n' "$stopped_by"
        if [ "$stopped_by" = "SIGTERM" ] && [ -n "${GITHUB_ACTIONS:-}" ]; then
            printf '::warning title=Host smoke (%s): SIGINT did not stop the host::SIGTERM did. \
Ctrl-C at a terminal is the path ShutdownCoordinator hooks through Console.CancelKeyPress, and on \
this platform it did not end the process.\n' "$(uname -s)"
        fi
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

# ---------------------------------------------------------------------------
# Wide binding, and the credential it cannot be asked for without.
# ---------------------------------------------------------------------------
# This is the half of --listen network that cannot be checked on the development
# machine. Windows reserves wildcard prefixes, so an unelevated run there fails at
# the socket with a urlacl message before it ever serves a byte -- which proves the
# product's own gate let it through and nothing beyond that. Linux and macOS have no
# such reservation, so here the binding is real and every claim below is measured
# against a listener that is genuinely open to the network.
echo
echo "--- wide binding ---"

NET_PORT=$((PORT + 1))
NET_BASE="http://127.0.0.1:$NET_PORT"
NET_LOG="$HOST_DIR/smoke-network.log"
CRED="$HOST_DIR/smoke.cred"
# Not a constant in the repository: this credential exists for the next twenty seconds
# on a throwaway runner, and a fixed string is one somebody eventually copies.
SECRET="smoke-$$-$PORT"

refusal=$("$BIN" --simulate --port "$NET_PORT" --listen network 2>&1)
refused=$?
# The refusal must name the flag that fixes it. An operator told only that something is
# missing reaches for whatever silences the message, and here the silencing flag is the
# one that closes the console back down.
if [ "$refused" -ne 0 ] && printf '%s' "$refusal" | grep -q -- "--credential"; then
    pass "--listen network without a credential is refused before anything binds"
else
    fail "--listen network without a credential is refused before anything binds" \
         "exit=$refused output=$(printf '%s' "$refusal" | tr '\r\n' '  ' | cut -c1-200)"
fi

printf '%s\n' "$SECRET" | "$BIN" credential --out "$CRED" --force > "$HOST_DIR/cred.log" 2>&1
check "a credential can be enrolled without a desktop" "$([ -s "$CRED" ] && echo 0 || echo 1)" \
      "$(tail -3 "$HOST_DIR/cred.log" 2>/dev/null | tr '\r\n' '  ')"

set -m
"$BIN" --simulate --port "$NET_PORT" --listen network --credential "$CRED" > "$NET_LOG" 2>&1 &
NET_PID=$!
set +m
cleanup() {
    for p in "$HOST_PID" "${NET_PID:-}"; do
        [ -n "$p" ] && kill -0 "$p" 2>/dev/null && kill -KILL "$p" 2>/dev/null
    done
    return 0
}

# 401 is the ready signal here, not a failure: the host cannot authenticate to itself
# either, since it holds a PBKDF2 derivation and not the password.
net_ready=1
for _ in $(seq 1 60); do
    if ! kill -0 "$NET_PID" 2>/dev/null; then break; fi
    code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "$NET_BASE/api/status" 2>/dev/null)
    if [ "$code" = "401" ] || [ "$code" = "200" ]; then net_ready=0; break; fi
    sleep 1
done
# Windows is the one platform where failing here says nothing about the product.
# A wildcard prefix is a reserved resource there, so an unelevated run is refused by
# the OS -- after this host's own gate passed it, which is the part Windows can show.
# Reported as unmeasured rather than failed, for the same reason the signal section
# below does it: a red mark for something that was never attempted is as unfounded as
# a green one. This script runs on Linux and macOS in CI, which is where it counts.
if [ "$net_ready" -ne 0 ] && grep -q 'urlacl\|requires elevation' "$NET_LOG" 2>/dev/null; then
    echo '  NOTE  wide binding not checked: this OS reserves wildcard prefixes and this'
    echo '        run is not elevated. The product got as far as the socket, so its own'
    echo '        credential check passed; everything past the bind is unmeasured here.'
else
    check "the host bound every interface and answered" "$net_ready" \
          "$(tail -4 "$NET_LOG" 2>/dev/null | tr '\r\n' '  ')"
fi

if [ "$net_ready" -eq 0 ]; then
    anon=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$NET_BASE/api/status")
    check "an unauthenticated request to a wide-bound console is refused" \
          "$([ "$anon" = "401" ] && echo 0 || echo 1)" \
          "got $anon, and this listener is reachable from the whole segment"

    authed=$(curl -s -u "ignored:$SECRET" --max-time 5 "$NET_BASE/api/status" -o "$HOST_DIR/net-status.json" -w '%{http_code}')
    check "the credential opens it" "$([ "$authed" = "200" ] && echo 0 || echo 1)" "got $authed"

    python3 - "$HOST_DIR/net-status.json" <<'PY'
import json, sys
reach = json.load(open(sys.argv[1])).get("reachability") or {}
bad = []

def want(name, ok, detail):
    print(f"  {'PASS' if ok else 'FAIL'}  {name}")
    if not ok:
        print(f"        {detail}")
        bad.append(name)

want("the console reports itself as network-reachable", reach.get("scope") == "network",
     f"scope={reach.get('scope')!r} -- an operator checking whether they exposed it is told no")
want("it reports the credential as in force", reach.get("authenticated") is True,
     f"authenticated={reach.get('authenticated')!r}")
# The point of the field. Reporting authenticated without this beside it invites the
# reading that the password is private on the wire, and over Basic on plain HTTP it is not.
want("it does not claim the link is encrypted", reach.get("encrypted") is False,
     f"encrypted={reach.get('encrypted')!r} -- nothing here terminates TLS")
want("the prefixes it names are wildcards", any("+" in p for p in reach.get("prefixes") or []),
     f"prefixes={reach.get('prefixes')!r}")
sys.exit(1 if bad else 0)
PY
    check "the status payload describes the exposure honestly" "$?"

    # The strongest form of the claim: reached at an address another machine could use,
    # rather than at the loopback alias of a socket that happens to be bound wide.
    lan=""
    case "$(uname -s)" in
        Darwin) for i in en0 en1 en2; do lan=$(ipconfig getifaddr "$i" 2>/dev/null); [ -n "$lan" ] && break; done ;;
        Linux)  lan=$(hostname -I 2>/dev/null | tr ' ' '\n' | grep -E '^[0-9]+\.' | grep -v '^127\.' | head -1) ;;
    esac

    if [ -n "$lan" ]; then
        off=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "http://$lan:$NET_PORT/api/status")
        on=$(curl -s -u "ignored:$SECRET" -o /dev/null -w '%{http_code}' --max-time 5 "http://$lan:$NET_PORT/api/status")
        check "it answers on $lan, an address off this machine's loopback" \
              "$([ "$off" = "401" ] && [ "$on" = "200" ] && echo 0 || echo 1)" \
              "anonymous=$off authenticated=$on (wanted 401 then 200)"
    else
        # Said rather than skipped silently: the wildcard prefix above is evidence, and
        # this would have been proof. A harness that quietly drops its strongest check
        # reports the same green as one that ran it.
        echo '  NOTE  no non-loopback address found on this runner; the wildcard prefix is'
        echo '        the evidence of wide binding here, not a connection from off-box'
    fi
fi

kill -INT "$NET_PID" 2>/dev/null || true
for _ in $(seq 1 10); do kill -0 "$NET_PID" 2>/dev/null || break; sleep 1; done
kill -KILL "$NET_PID" 2>/dev/null || true
rm -f "$CRED"

echo "--- wide-binding host output ---"
sed 's/^/        /' "$NET_LOG"

echo "--- host output ---"
sed 's/^/        /' "$LOG"

echo
if [ "$failures" -eq 0 ]; then
    echo "=== host smoke test passed on $(uname -s) $(uname -m) ==="
    exit 0
fi
annotate "$failures check(s) failed" \
    "last lines of the host's own output: $(tail -6 "$LOG" 2>/dev/null | tr '\r\n' '  ')"
echo "=== host smoke test: $failures check(s) failed on $(uname -s) $(uname -m) ==="
exit 1
