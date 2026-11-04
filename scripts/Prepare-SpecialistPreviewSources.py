"""Import the exact QuickLook specialist reference sources used by S13.

These reviewable originals are distribution/provenance materials, not compiled
plugin assemblies. Adapted code stays under Preview/ and uses Pathweft's host.
"""
import concurrent.futures
import hashlib
import json
from pathlib import Path
import urllib.request
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PIN = "2ec219aa18416c6e3b89d6aad3d82d10610f917f"
MODULES = ("Csv", "Db", "Mail", "Cert", "App", "Thumbnail", "MediaInfo",
           "Binary", "ELF", "Dump", "Prefetch", "Chm", "CLSID", "Office")


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Pathweft-source-inventory"})
    with urllib.request.urlopen(request) as response:
        return response.read()


def main():
    tree = json.loads(fetch(f"https://api.github.com/repos/QL-Win/QuickLook/git/trees/{PIN}?recursive=1"))
    assert not tree.get("truncated"), "Incomplete upstream source inventory"
    prefixes = tuple(f"QuickLook.Plugin/QuickLook.Plugin.{name}Viewer/" for name in MODULES)
    paths = [item["path"] for item in tree["tree"] if item["path"].startswith(prefixes)
             and item["path"].endswith((".cs", ".csproj", ".xaml"))]
    destination = ROOT / "vendor" / "QuickLook"
    manifest_path = destination / "source-manifest.json"
    manifest = {item["file"]: item for item in json.loads(manifest_path.read_text("utf-8"))}

    def obtain(path):
        url = f"https://raw.githubusercontent.com/QL-Win/QuickLook/{PIN}/{path}"
        output = destination / "original" / (path + ".txt")
        if output.exists() and path in manifest:
            data = output.read_bytes()
            assert hashlib.sha256(data).hexdigest().upper() == manifest[path]["sha256"].upper(), path
        else:
            data = fetch(url)
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(data)
        return {"file": path, "commit": PIN, "url": url, "sha256": hashlib.sha256(data).hexdigest().upper()}

    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        for item in pool.map(obtain, paths):
            manifest[item["file"]] = item
    manifest_path.write_text(json.dumps(list(manifest.values()), indent=2) + "\n", "utf-8", newline="\n")
    packages = {}
    for name in MODULES:
        project = destination / "original" / f"QuickLook.Plugin/QuickLook.Plugin.{name}Viewer/QuickLook.Plugin.{name}Viewer.csproj.txt"
        doc = ET.fromstring(project.read_text("utf-8-sig"))
        packages[name] = [{"id": p.attrib["Include"].strip(), "version": p.attrib.get("Version")}
                          for p in doc.iter("PackageReference")]
    print(json.dumps({"commit": PIN, "imported_files": len(paths), "upstream_packages": packages}, indent=2))


if __name__ == "__main__":
    main()
