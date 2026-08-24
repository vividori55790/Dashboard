#!/usr/bin/env python3
"""Turns .trx files into the list of tests that failed, in the run's own summary.

Written because the first CI failure could not be diagnosed from outside. All three
jobs failed at the same step, the annotation said only "Process completed with exit
code 1", the log endpoint answers 403 without a token, and the .trx artifacts need
one to download -- so the run said that something broke and nothing about what.

GITHUB_STEP_SUMMARY renders on the run page and is readable by anyone who can see
the repository, which is the property that matters: a failure nobody can read is a
failure nobody can act on, and the point of putting the suite in CI was to stop
depending on who happened to be at the machine.

Usage:  trx-summary.py <directory of .trx files> [label]
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET

# The same guard verify_live.py carries, and for a reason this script proved on its
# first local run: printing an em dash on the Windows runner raised UnicodeEncodeError
# against the console's legacy codepage, and the step whose whole job is explaining a
# failure would have become a second one. A test name or assertion message can contain
# anything, so errors="replace" rather than a promise that it will not.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

directory = sys.argv[1] if len(sys.argv) > 1 else "testresults"
label = sys.argv[2] if len(sys.argv) > 2 else os.environ.get("RUNNER_OS", "")

files = sorted(glob.glob(os.path.join(directory, "**", "*.trx"), recursive=True))
if not files:
    print(f"no .trx under {directory!r} -- the test step produced no results file")
    sys.exit(0)

annotations = []
lines = [f"## Test results — {label}".rstrip(), ""]
total_failed = 0

for path in files:
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as error:
        lines.append(f"- `{os.path.basename(path)}` could not be parsed: {error}")
        continue

    counters = root.find("t:ResultSummary/t:Counters", NS)
    if counters is not None:
        executed = counters.get("executed", "?")
        passed = counters.get("passed", "?")
        failed = counters.get("failed", "0")
        lines.append(f"**{os.path.basename(path)}** — {passed} passed, {failed} failed, {executed} executed")
    lines.append("")

    for result in root.findall("t:Results/t:UnitTestResult", NS):
        if result.get("outcome") != "Failed":
            continue
        total_failed += 1
        name = result.get("testName", "(unnamed)")
        duration = result.get("duration", "")
        message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS) or ""
        stack = result.findtext("t:Output/t:ErrorInfo/t:StackTrace", default="", namespaces=NS) or ""

        # The first stack frame inside the repository is what names the test's own
        # line; everything above it is the assertion library explaining itself.
        frame = next((ln.strip() for ln in stack.splitlines()
                      if "TelemetryDashboard" in ln and " in " in ln), "")

        lines.append(f"<details><summary><b>{name}</b> ({duration})</summary>")
        lines.append("")
        lines.append("```")
        lines.append(message.strip()[:2000] or "(no message)")
        if frame:
            lines.append("")
            lines.append(frame[:400])
        lines.append("```")
        lines.append("</details>")
        lines.append("")

        # Also as an annotation. GITHUB_STEP_SUMMARY turned out not to render for a
        # reader who is not signed in, which defeats the purpose for a public
        # repository -- annotations do, and they were the only part of the first two
        # failed runs that could be read from outside. Newlines have to be encoded or
        # the command is truncated at the first one.
        detail = (message.strip() + (("\n" + frame) if frame else ""))[:800]
        detail = detail.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        annotations.append(f"::error title=Test failed: {name}::{detail}")

if total_failed == 0:
    lines.append("No failures recorded in the results files.")

report = "\n".join(lines)
print(report)

summary = os.environ.get("GITHUB_STEP_SUMMARY")
if summary:
    with open(summary, "a", encoding="utf-8") as handle:
        handle.write(report + "\n")

# Annotations last, so they are not buried in the report above. GitHub keeps only
# the first ten error annotations per step, so the cap is stated rather than
# discovered: a truncated list that does not say it is truncated reads as the whole
# answer, which is the failure mode this repository names most often.
LIMIT = 10
for annotation in annotations[:LIMIT]:
    print(annotation)
if len(annotations) > LIMIT:
    print(f"::notice::{len(annotations) - LIMIT} further failure(s) not annotated; "
          f"the full list is in this step's log and in the testresults artifact")
