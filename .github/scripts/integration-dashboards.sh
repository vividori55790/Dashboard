#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Does a real Prometheus store what this hub serves, and does a real Grafana
# import the dashboard it generates?
#
# Both endpoints were shipped with that question open, and the ARCHITECTURE
# rows said so: the exposition had been read back by the Prometheus project's
# own Python parser, and the dashboard validated against a JSON schema. Two
# parsers agreeing is weaker than a scraper storing a series, and a schema
# passing is weaker than Grafana accepting the file. Neither could be settled on
# the development machine, where the Docker engine does not start.
#
# It is worth a whole job because the failure it guards is the quiet one. The
# moment an operator points their stack at /metrics, the metric names and their
# labels become a contract their alert rules depend on -- and when it breaks,
# the endpoint keeps serving, the dashboard keeps importing, and only an empty
# panel says anything. That exact break happened once already, between two
# pieces of work that each passed their own tests.
# ---------------------------------------------------------------------------
set -uo pipefail

HOST_DIR=${1:?usage: integration-dashboards.sh <published-host-directory> [port]}
PORT=${2:-8099}
PROM_PORT=$((PORT + 10))
GRAFANA_PORT=$((PORT + 11))

BIN="$HOST_DIR/TelemetryDashboard.Host"
LOG="$HOST_DIR/integration-host.log"
WORK="$HOST_DIR/integration"
mkdir -p "$WORK"

failures=0
pass() { printf '  PASS  %s\n' "$1"; }

annotate() {
    [ -n "${GITHUB_ACTIONS:-}" ] || return 0
    printf '::error title=Dashboard integration: %s::%s\n' "$1" \
        "$(printf '%s' "${2:-no detail}" | sed 's/%/%25/g' | tr '\r\n' '  ')"
}

notice() {
    [ -n "${GITHUB_ACTIONS:-}" ] || return 0
    printf '::notice title=Dashboard integration: %s::%s\n' "$1" \
        "$(printf '%s' "${2:-}" | sed 's/%/%25/g' | tr '\r\n' '  ')"
}

fail() {
    printf '  FAIL  %s\n' "$1"
    [ $# -gt 1 ] && printf '        %s\n' "$2"
    annotate "$1" "${2:-}"
    failures=$((failures + 1))
}

check() { if [ "$2" -eq 0 ]; then pass "$1"; else fail "$1" "${3:-}"; fi }

cleanup() {
    docker rm -f td-prometheus td-grafana > /dev/null 2>&1 || true
    [ -n "${HOST_PID:-}" ] && kill -KILL "$HOST_PID" 2>/dev/null
    return 0
}
trap cleanup EXIT

echo "=== dashboard integration: a real Prometheus and a real Grafana ==="

[ -f "$BIN" ] || { echo "  FAIL  no published host at $BIN"; exit 1; }
chmod +x "$BIN"

set -m
"$BIN" --simulate --port "$PORT" > "$LOG" 2>&1 &
HOST_PID=$!
set +m

ready=1
for _ in $(seq 1 60); do
    if ! kill -0 "$HOST_PID" 2>/dev/null; then
        echo "  FAIL  the host exited before serving:"; sed 's/^/        /' "$LOG"; exit 1
    fi
    curl -fsS --max-time 2 "http://127.0.0.1:$PORT/metrics" -o "$WORK/metrics.txt" 2>/dev/null && { ready=0; break; }
    sleep 1
done
check "the host serves /metrics" "$ready" "sixty seconds with no reply; see the host log"
[ "$ready" -eq 0 ] || exit 1

# Let the simulator produce enough for a scrape to be worth storing.
sleep 8

# ---------------------------------------------------------------------------
# Prometheus
# ---------------------------------------------------------------------------
# --network host so the container can reach a host process on 127.0.0.1. The
# alternative, host.docker.internal, is not wired on Linux runners.
cat > "$WORK/prometheus.yml" <<YML
global:
  scrape_interval: 2s
scrape_configs:
  - job_name: telemetrydashboard
    metrics_path: /metrics
    static_configs:
      - targets: ['127.0.0.1:$PORT']
YML

docker run -d --name td-prometheus --network host \
    -v "$WORK/prometheus.yml:/etc/prometheus/prometheus.yml:ro" \
    prom/prometheus:latest \
    --config.file=/etc/prometheus/prometheus.yml \
    --web.listen-address=":$PROM_PORT" > /dev/null 2>&1
started=$?
check "a Prometheus container starts" "$started" "docker run failed; see the job log"
[ "$started" -eq 0 ] || exit 1

up=1
for _ in $(seq 1 60); do
    curl -fsS --max-time 2 "http://127.0.0.1:$PROM_PORT/-/ready" > /dev/null 2>&1 && { up=0; break; }
    sleep 1
done
check "it becomes ready" "$up" "$(docker logs td-prometheus 2>&1 | tail -5 | tr '\r\n' '  ')"

# It has to actually scrape, not merely start. up==1 for our job is the target
# reporting healthy from Prometheus's own point of view rather than from ours.
scraped=1
for _ in $(seq 1 40); do
    if curl -fsS --max-time 3 \
        "http://127.0.0.1:$PROM_PORT/api/v1/query?query=up%7Bjob%3D%22telemetrydashboard%22%7D" \
        -o "$WORK/up.json" 2>/dev/null && grep -q '"value"' "$WORK/up.json" \
        && grep -q '"1"' "$WORK/up.json"; then scraped=0; break; fi
    sleep 2
done
check "Prometheus reports the target up" "$scraped" \
      "$(cat "$WORK/up.json" 2>/dev/null | head -c 300)"

# ---------------------------------------------------------------------------
# The contract: the dashboard's own queries, answered by the stored series
# ---------------------------------------------------------------------------
curl -fsS --max-time 10 "http://127.0.0.1:$PORT/api/export/grafana" -o "$WORK/dashboard.json"
check "the host generates a dashboard" "$?" "the export endpoint did not answer"

python3 - "$WORK/dashboard.json" "$PROM_PORT" <<'PY' | tee "$WORK/contract.txt"
import json, sys, urllib.parse, urllib.request

dashboard = json.load(open(sys.argv[1], encoding="utf-8"))
port = sys.argv[2]

def walk(node, found):
    if isinstance(node, dict):
        for key, value in node.items():
            if key == "expr" and isinstance(value, str):
                found.append(value)
            walk(value, found)
    elif isinstance(node, list):
        for item in node:
            walk(item, found)

queries = []
walk(dashboard, queries)
queries = [q for q in queries if "{" in q]

bad = []

def want(name, ok, detail):
    print(f"  {'PASS' if ok else 'FAIL'}  {name}")
    if not ok:
        print(f"        {detail}")
        bad.append((name, detail))

want("the dashboard contains queries at all", len(queries) > 0,
     "a dashboard with no panels would make every assertion below vacuous")

answered = 0
for query in queries:
    url = f"http://127.0.0.1:{port}/api/v1/query?query=" + urllib.parse.quote(query)
    try:
        with urllib.request.urlopen(url, timeout=10) as response:
            body = json.load(response)
    except Exception as error:            # noqa: BLE001 - reported, not swallowed
        want(f"query resolves: {query}", False, f"request failed: {error}")
        continue

    results = body.get("data", {}).get("result", [])
    if results:
        answered += 1
    else:
        want(f"a stored series answers: {query}", False,
             "Prometheus accepted the query and has no series for it. A panel that "
             "imports cleanly and draws nothing is the failure this job exists for.")

want(f"every generated query is answered by a stored series ({answered}/{len(queries)})",
     answered == len(queries) and len(queries) > 0,
     f"{len(queries) - answered} of {len(queries)} matched nothing")

sys.exit(1 if bad else 0)
PY
# PIPESTATUS[0], not $?. Piping the block above through tee makes $? tee's exit,
# and tee always succeeds -- the contract check would have passed no matter what
# Prometheus answered. pipefail happens to save it here; not depending on that.
contract=${PIPESTATUS[0]}
check "the generated dashboard matches what Prometheus stored" "$contract"

# The count, not just the verdict. Annotations are the only part of a run readable
# without an account, and "all of them" is the fact worth reading -- a job that
# checked one query and a job that checked forty are the same green otherwise.
if [ "$contract" -eq 0 ]; then
    notice "every generated query resolved against a stored series" \
           "$(grep -o '([0-9]*/[0-9]*)' "$WORK/contract.txt" 2>/dev/null | tail -1)"
fi

# ---------------------------------------------------------------------------
# Grafana
# ---------------------------------------------------------------------------
docker run -d --name td-grafana --network host \
    -e GF_SERVER_HTTP_PORT="$GRAFANA_PORT" \
    -e GF_SECURITY_ADMIN_PASSWORD=admin \
    -e GF_AUTH_ANONYMOUS_ENABLED=false \
    grafana/grafana:latest > /dev/null 2>&1
started=$?
check "a Grafana container starts" "$started"

if [ "$started" -eq 0 ]; then
    healthy=1
    for _ in $(seq 1 90); do
        curl -fsS --max-time 2 "http://127.0.0.1:$GRAFANA_PORT/api/health" > /dev/null 2>&1 \
            && { healthy=0; break; }
        sleep 1
    done
    check "it becomes healthy" "$healthy" \
          "$(docker logs td-grafana 2>&1 | tail -5 | tr '\r\n' '  ')"

    if [ "$healthy" -eq 0 ]; then
        # Wrapped the way the API wants it. A dashboard that imports is the claim;
        # the response body is kept because Grafana's refusals name the field.
        python3 - "$WORK/dashboard.json" "$WORK/import.json" <<'PY'
import json, sys
dashboard = json.load(open(sys.argv[1], encoding="utf-8"))
json.dump({"dashboard": dashboard, "overwrite": True, "folderId": 0},
          open(sys.argv[2], "w", encoding="utf-8"))
PY

        code=$(curl -s -o "$WORK/import-response.json" -w '%{http_code}' --max-time 20 \
            -u admin:admin -H "Content-Type: application/json" \
            -X POST "http://127.0.0.1:$GRAFANA_PORT/api/dashboards/db" \
            --data-binary "@$WORK/import.json")

        check "Grafana imports the generated dashboard" \
              "$([ "$code" = "200" ] && echo 0 || echo 1)" \
              "HTTP $code: $(head -c 400 "$WORK/import-response.json" 2>/dev/null)"

        if [ "$code" = "200" ]; then
            uid=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1],encoding='utf-8')).get('uid',''))" "$WORK/import-response.json")
            # Read it back rather than trusting the 200. An import that stores
            # nothing and an import that stores the file both answer 200.
            back=$(curl -s -o "$WORK/readback.json" -w '%{http_code}' --max-time 10 \
                -u admin:admin "http://127.0.0.1:$GRAFANA_PORT/api/dashboards/uid/$uid")
            panels=$(python3 -c "
import json,sys
d=json.load(open(sys.argv[1],encoding='utf-8')).get('dashboard',{})
print(len(d.get('panels',[])))" "$WORK/readback.json" 2>/dev/null || echo 0)

            check "the stored dashboard reads back with its panels" \
                  "$([ "$back" = "200" ] && [ "${panels:-0}" -gt 0 ] && echo 0 || echo 1)" \
                  "HTTP $back, $panels panels"
            notice "imported into Grafana" "uid=$uid, $panels panels stored"
        fi
    fi
fi

echo "--- host output ---"
sed 's/^/        /' "$LOG"

echo
if [ "$failures" -eq 0 ]; then
    echo "=== a real Prometheus stored it and a real Grafana imported it ==="
    exit 0
fi
annotate "$failures check(s) failed" "see the step log"
echo "=== $failures check(s) failed ==="
exit 1
