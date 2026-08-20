"""Checks XAML without building, so several people can work in one tree at once.

A full `dotnet build` locks obj/ and bin/, and concurrent builds of the same project fail with
MSB3021 file-lock errors that look nothing like the mistake that caused them. This catches the two
things that actually go wrong while editing XAML by hand -- malformed XML, and a StaticResource
naming a key that no dictionary defines -- and it needs no lock at all.

Usage:  python Themes/verify_xaml.py [files...]        (default: every .xaml in the project)
"""
import glob
import os
import re
import sys
import xml.dom.minidom

# The Korean console runs cp949, which cannot encode the very characters this script exists to
# report. Reconfigure rather than print them raw, and name them by codepoint besides.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
THEME_FILES = ["Tokens.xaml", "Typography.xaml", "Controls.xaml"]


def defined_keys():
    """Every x:Key the theme dictionaries and App.xaml declare."""
    keys = set()
    sources = [os.path.join(HERE, name) for name in THEME_FILES]
    sources.append(os.path.join(PROJECT, "App.xaml"))
    for path in sources:
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as handle:
            keys.update(re.findall(r'x:Key="([^"]+)"', handle.read()))
    return keys


def referenced_keys(text):
    """StaticResource and DynamicResource lookups, both attribute and element syntax."""
    return set(re.findall(r'\{(?:Static|Dynamic)Resource\s+([A-Za-z0-9_.]+)\s*\}', text))


def main():
    targets = sys.argv[1:] or sorted(
        p for p in glob.glob(os.path.join(PROJECT, "**", "*.xaml"), recursive=True)
        if f"{os.sep}obj{os.sep}" not in p and f"{os.sep}bin{os.sep}" not in p
    )

    known = defined_keys()
    failures = 0

    for path in targets:
        rel = os.path.relpath(path, PROJECT)
        with open(path, encoding="utf-8") as handle:
            text = handle.read()

        try:
            xml.dom.minidom.parseString(text.encode("utf-8"))
        except Exception as error:  # noqa: BLE001 - the message is the whole point
            print(f"FAIL  {rel}\n      malformed XML: {error}")
            failures += 1
            continue

        # A key defined in the same file is fine; only cross-file lookups need the dictionaries.
        local = set(re.findall(r'x:Key="([^"]+)"', text))
        missing = sorted(k for k in referenced_keys(text) if k not in known and k not in local)
        if missing:
            print(f"FAIL  {rel}\n      undefined resource keys: {', '.join(missing)}")
            failures += 1
            continue

        emoji = sorted(set(re.findall(r'[\U0001F000-\U0001FAFF☀-➿️]', text)))
        codes = " ".join(f"U+{ord(c):04X}" for c in emoji)
        note = f"   (emoji still present: {codes})" if emoji else ""
        print(f"ok    {rel}{note}")

    print(f"\n{len(targets)} file(s), {failures} failure(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
