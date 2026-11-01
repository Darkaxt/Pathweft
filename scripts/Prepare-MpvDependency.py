"""Import only the pinned library, its notices and corresponding build inputs.

The source archive is the user's existing verified VLC-fork release. No player
executable, Qt interface or user configuration is imported into Pathweft.
"""
import argparse
import hashlib
import json
import shutil
import tarfile
import urllib.request
from pathlib import Path

PINS = {"libmpv-2.dll": "1a041ddf907e806c462ba3df7ca9b2e213aa6cfcc28647d8afea43538a37e806",
        "libspirv-cross-c-shared.dll": "4a755f3b9ce13bb0290028bb9ac4be4e59e3dabce15ed15571d2370706a0b746"}
SOURCE_SHA = "a49f59d03beec40a8142e313cdc310cced4d3f84f06e644d5055fb133f8dc210"
SDK_NAMES = {"freetype-2.14.3.tar.xz", "fribidi-1.0.16.tar.xz", "harfbuzz-14.2.1.tar.xz",
             "libass-0.17.5.tar.xz", "libiconv-1.18.tar.gz", "zlib-1.3.2.tar.xz", "mingw-runtime-source.tar.gz"}

def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()

def checked_copy(source, target, expected):
    if digest(source) != expected.lower():
        raise ValueError(f"Pinned input differs: {source}")
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)
    if digest(target) != expected.lower():
        raise ValueError(f"Copied input differs: {target}")

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--donor-release", type=Path, required=True)
    parser.add_argument("--donor-source", type=Path, required=True)
    parser.add_argument("--cache", type=Path, default=Path("D:/BuildCache/Pathweft/mpv/e471496c56-pathweft.1"))
    args = parser.parse_args()
    cache = args.cache.resolve()
    if not cache.is_relative_to(Path("D:/BuildCache/Pathweft/mpv").resolve()):
        raise ValueError("Use the explicit persistent Pathweft dependency cache")
    build = json.loads((args.donor_release / "MPV-BUILDINFO.json").read_text())
    for name, expected in PINS.items():
        if next(item["sha256"] for item in build["files"] if item["name"] == name) != expected:
            raise ValueError("The donor runtime metadata differs from the reviewed release")
        checked_copy(args.donor_release / name, cache / name, expected)
    shutil.copyfile(args.donor_release / "MPV-BUILDINFO.json", cache / "MPV-BUILDINFO.json")
    manifest_path = args.donor_release / "licenses/dependencies/manifest.json"
    if digest(args.donor_release / "licenses/dependencies/baseline-sdk-manifest.json") != build["sdk_dependency_notices_manifest_sha256"]:
        raise ValueError("The donor's dependency notice inventory differs")
    if digest(manifest_path) != "c70c2eef06ba85d9f4c40a409063ebd015ecc633ff1f5c88c42900d10d025ce9":
        raise ValueError("The donor's merged MPV/SDK notice inventory differs")
    notices = json.loads(manifest_path.read_text())
    wanted = {item["archive"]: item for item in build["sources"] + build["host_build_tools"]}
    for item in notices["archives"]:
        if item["archive"] in SDK_NAMES:
            wanted[item["archive"]] = item
    if not SDK_NAMES.issubset(wanted):
        raise ValueError("Missing linked SDK source inventory")
    selected = [item for item in notices["archives"] if item["archive"] in wanted]
    missing_notices = set(wanted) - {item["archive"] for item in selected}
    if missing_notices - {item["archive"] for item in build["host_build_tools"]}:
        raise ValueError("Missing linked-library license coverage: " + repr(sorted(missing_notices)))
    for item in selected:
        if item["sha256"] != wanted[item["archive"]]["sha256"]:
            raise ValueError("Source and notice identities differ")
        for notice in item["notices"]:
            checked_copy(args.donor_release / "licenses/dependencies" / notice["file"],
                         cache / "licenses" / notice["file"], notice["sha256"])
    (cache / "licenses/manifest.json").write_text(json.dumps({"archives": selected}, indent=2))
    source_root = cache / "sources"
    source_root.mkdir(exist_ok=True)
    for item in build["host_build_tools"]:
        target = source_root / item["archive"]
        if not target.is_file():
            with urllib.request.urlopen(item["url"]) as source, target.open("wb") as output:
                shutil.copyfileobj(source, output)
        if digest(target) != item["sha256"]:
            raise ValueError("Build-only source identity differs")
    missing = {name for name, item in wanted.items()
               if not (source_root / name).is_file() or digest(source_root / name) != item["sha256"]}
    recipes = source_root / "build-recipes"
    if missing or not (recipes / "tools/windows-player/build-mpv-runtime.sh").is_file():
        if digest(args.donor_source) != SOURCE_SHA:
            raise ValueError("The full corresponding-source release differs")
        with tarfile.open(args.donor_source, "r|gz") as archive:
            for member in archive:
                if not member.isfile():
                    continue
                name = Path(member.name).name
                relative = None
                if name in missing:
                    relative = Path(name)
                elif "/contrib/src/" in member.name:
                    relative = Path("build-recipes/contrib/src") / member.name.split("/contrib/src/", 1)[1]
                elif "/tools/windows-player/" in member.name:
                    relative = Path("build-recipes/tools/windows-player") / member.name.split("/tools/windows-player/", 1)[1]
                if relative is None:
                    continue
                if ".." in relative.parts:
                    raise ValueError("Invalid source archive member")
                target = source_root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.extractfile(member) as source, target.open("wb") as output:
                    shutil.copyfileobj(source, output)
                if name in missing:
                    if digest(target) != wanted[name]["sha256"]:
                        raise ValueError(f"Corresponding source differs: {name}")
                    missing.remove(name)
        if missing:
            raise ValueError(f"Missing corresponding sources: {sorted(missing)}")
    if digest(recipes / "tools/windows-player/build-mpv-runtime.sh") != build["build_script_sha256"]:
        raise ValueError("Pinned native build recipe differs")
    if digest(recipes / "tools/windows-player/patches/mpv-wasapi-session.patch") != build["patch_sha256"]:
        raise ValueError("Pinned native patch differs")
    receipt = {"schema": 1, "donor": "https://github.com/Darkaxt/vlc", "donorTag": "redesign-v1.1.1",
               "donorCommit": "b84e7326e3d3e554120f3866fbe1c1acfa5ec6cc", "runtime": PINS,
               "mpvCommit": "e471496c56f0f49c133bd88b50aed62b845f1909",
               "sourceArchiveSha256": SOURCE_SHA, "sources": [{"name": name, "sha256": item["sha256"]} for name, item in sorted(wanted.items())],
               "output": "gpu-next/D3D11/libplacebo, BT.709 sRGB SDR; no native DV signaling or full P7 FEL claim"}
    (cache / "runtime-manifest.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps({"cache": str(cache), "libraries": list(PINS), "sourceArchives": len(wanted)}))

if __name__ == "__main__":
    main()
