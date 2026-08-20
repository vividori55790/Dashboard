"""Runs the real host against real public data feeds and checks what actually came out.

Every other test in this repository exercises the code with data the test itself made up. This one
does not: it starts the published binary, points it at infrastructure nobody here controls, and
asserts on what arrives. That is the only kind of check that can catch the things the unit tests
structurally cannot -- a missing User-Agent header, a start-up banner that describes a source it
does not recognise, a forecast that is mathematically fine and physically impossible. All three of
those were found this way, by data that did not care what the code expected.

It is deliberately assertive rather than descriptive. A script that prints numbers invites someone
to skim them; a script that fails states which invariant broke.

Usage:  python verify_live.py [--host <path to TelemetryDashboard.Host.exe>]
"""
import argparse
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.dirname(os.path.abspath(__file__))
DEFAULT_HOST = os.path.join(
    ROOT, "TelemetryDashboard", "TelemetryDashboard.Host",
    "bin", "Debug", "net8.0", "TelemetryDashboard.Host.exe")
MAPS = os.path.join(ROOT, "TelemetryDashboard", "TelemetryDashboard.Host", "channel-maps")

PASS, FAIL = "PASS", "FAIL"
results = []


def check(name, ok, detail=""):
    results.append((PASS if ok else FAIL, name, detail))
    print(f"  {PASS if ok else FAIL}  {name}" + (f"\n        {detail}" if detail else ""))
    return ok


def get_json(url, timeout=10):
    with urllib.request.urlopen(url, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def read_frames(url, seconds, limit=4000):
    """Collects SSE payloads from our own host for a fixed wall-clock window."""
    frames, deadline = [], time.time() + seconds
    try:
        with urllib.request.urlopen(url, timeout=seconds + 5) as response:
            for raw in response:
                if time.time() > deadline or len(frames) >= limit:
                    break
                line = raw.decode("utf-8", "replace").strip()
                if not line.startswith("data:"):
                    continue
                try:
                    frames.append(json.loads(line[5:].strip()))
                except json.JSONDecodeError:
                    pass
    except (urllib.error.URLError, TimeoutError, OSError):
        pass
    return frames


def run_case(title, host, args, port, settle, collect):
    print(f"\n=== {title} ===")
    process = subprocess.Popen(
        [host, *args, "--port", str(port)],
        cwd=os.path.dirname(host),
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    try:
        time.sleep(settle)
        status = get_json(f"http://localhost:{port}/api/status")
        frames = read_frames(f"http://localhost:{port}/stream", collect)
        return status, frames
    finally:
        process.kill()
        process.wait(timeout=10)


def assert_honesty(frames, expect_simulated):
    """Invariants that must hold for every frame, whatever the source."""
    telemetry = [f for f in frames if "variable" in f]
    check("frames carry measurements", len(telemetry) > 0, f"{len(telemetry)} telemetry frames")
    if not telemetry:
        return telemetry

    check("synthetic marking matches the source",
          all(f.get("simulated") is expect_simulated for f in telemetry),
          f"expected simulated={expect_simulated} on all {len(telemetry)}")

    # The rule this project exists for: a sample the engine has not judged must not carry a verdict.
    unjudged = [f for f in telemetry if "analyzerId" not in f]
    check("unjudged samples carry no anomaly score",
          all("anomalyScore" not in f for f in unjudged),
          f"{len(unjudged)} of {len(telemetry)} still in warm-up, none of them scored")

    # A forecast is only permitted where a trend was actually fitted.
    forecast = [f for f in telemetry if "predicted60s" in f]
    check("no forecast without a verdict",
          all("analyzerId" in f for f in forecast),
          f"{len(forecast)} of {len(telemetry)} frames carry a forecast")

    check("every scored frame names its analyzer",
          all(f.get("analyzerId") for f in telemetry if "anomalyScore" in f))
    return telemetry


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default=DEFAULT_HOST)
    options = parser.parse_args()

    if not os.path.exists(options.host):
        print(f"host binary not found: {options.host}")
        return 2

    print("Verifying against live public infrastructure. Requires network access.")

    # --- 1. A real Server-Sent Events feed ----------------------------------
    status, frames = run_case(
        "Wikimedia EventStreams (SSE, no auth)", options.host,
        ["--sse", "https://stream.wikimedia.org/v2/stream/recentchange",
         "--stream-map", os.path.join(MAPS, "wikimedia-recentchange.json")],
        18301, settle=12, collect=10)

    check("host discovered channels from a live feed",
          status.get("seriesChannels", 0) > 0, f"seriesChannels={status.get('seriesChannels')}")
    check("samples were accepted, none refused",
          status.get("seriesSamplesAccepted", 0) > 0 and status.get("seriesSamplesRefused", 1) == 0,
          f"accepted={status.get('seriesSamplesAccepted')} refused={status.get('seriesSamplesRefused')}")
    telemetry = assert_honesty(frames, expect_simulated=False)
    check("more than one reporting node was seen",
          len({f.get("nodeId") for f in telemetry}) > 1,
          f"{len({f.get('nodeId') for f in telemetry})} distinct nodes")

    # --- 2. A polled REST endpoint ------------------------------------------
    status, frames = run_case(
        "USGS earthquake feed (polled JSON)", options.host,
        ["--poll", "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_hour.geojson",
         "--poll-interval", "2",
         "--stream-map", os.path.join(MAPS, "usgs-earthquakes.json")],
        18302, settle=10, collect=8)

    check("polled endpoint produced channels",
          status.get("seriesChannels", 0) > 0, f"seriesChannels={status.get('seriesChannels')}")
    assert_honesty(frames, expect_simulated=False)

    # --- 3. The simulator, which must be marked everywhere -------------------
    status, frames = run_case(
        "Built-in simulator (synthetic, must be labelled)", options.host,
        ["--simulate"], 18303, settle=6, collect=5)

    telemetry = assert_honesty(frames, expect_simulated=True)
    check("synthetic node ids keep the SIM: prefix",
          all(str(f.get("nodeId", "")).startswith("SIM:") for f in telemetry),
          "the mark has to survive onto the wire, not just exist in memory")

    print("\n" + "=" * 62)
    failed = [r for r in results if r[0] == FAIL]
    print(f" {len(results) - len(failed)} passed, {len(failed)} failed")
    for _, name, detail in failed:
        print(f"   FAILED: {name} {detail}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
