"""Report newer stable versions of the dependencies packaged with HISA."""

import json
import os
from pathlib import Path
import re
from urllib.request import Request, urlopen


DEPENDENCIES = (
    ("libstrophe", "strophe", "strophe/libstrophe", r"(\d+)\.(\d+)\.(\d+)"),
    ("OpenSSL 3.5 LTS", "openssl", "openssl/openssl", r"openssl-(3)\.(5)\.(\d+)"),
    ("Expat", "expat", "libexpat/libexpat", r"R_(\d+)_(\d+)_(\d+)"),
)


def latest_stable(repository, tag_pattern):
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "HISA-XMPP-dependency-check"}
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"

    request = Request(f"https://api.github.com/repos/{repository}/releases?per_page=100", headers=headers)
    with urlopen(request, timeout=30) as response:
        releases = json.load(response)

    versions = []
    for release in releases:
        match = re.fullmatch(tag_pattern, release["tag_name"])
        if match and not release["draft"] and not release["prerelease"]:
            versions.append((tuple(map(int, match.groups())), release["html_url"]))
    if not versions:
        raise RuntimeError(f"No matching stable release found for {repository}")
    return max(versions)


def main():
    pins = Path(__file__).with_name("build-native.sh").read_text(encoding="utf-8")
    summary = ["## Bundled XMPP dependency versions", ""]
    for name, pin_name, repository, tag_pattern in DEPENDENCIES:
        match = re.search(rf"^{pin_name}_version=(\d+)\.(\d+)\.(\d+)$", pins, re.MULTILINE)
        if not match:
            raise RuntimeError(f"Missing stable version pin for {name}")
        pinned = tuple(map(int, match.groups()))
        available, url = latest_stable(repository, tag_pattern)
        version = ".".join(map(str, available))
        update = " (update available)" if available > pinned else ""
        line = f"- {name} {match[0].split('=', 1)[1]} packaged, [{version}]({url}) upstream{update}"
        summary.append(line)
        if available > pinned and os.environ.get("GITHUB_ACTIONS") == "true":
            print(f"::warning::{name} {version} is available. Review its changes for HISA.")
    print("\n".join(summary))
    if summary_file := os.environ.get("GITHUB_STEP_SUMMARY"):
        with Path(summary_file).open("a", encoding="utf-8") as output:
            output.write("\n".join(summary) + "\n")


if __name__ == "__main__":
    main()
