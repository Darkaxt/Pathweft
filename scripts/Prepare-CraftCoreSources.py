"""Extract the exact reviewed donor sources for the native bridge build.

Compiler/source trees use D: scratch for sustained small-file IO. Archives in
D:/BuildCache remain reproducible dependency inputs. No upstream file is edited.
"""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--scratch-root',type=Path,required=True)
args=parser.parse_args()
scratch=args.scratch_root.resolve()
if not str(scratch).lower().startswith('d:\\temp\\pathweft-s13-s9-s11\\craft-cores'):
    raise ValueError('Use the registered exact Craft compiler scratch root')
manifest=json.loads((ROOT/'vendor/Office/runtime-manifest.json').read_text())
for app in manifest['apps']:
    archive_path=Path('D:/BuildCache/Pathweft/office/sources')/app['sourceArchive']
    if hashlib.file_digest(archive_path.open('rb'),'sha256').hexdigest()!=app['sourceSha256']:
        raise ValueError('Donor archive hash mismatch: '+app['id'])
    destination=scratch/'sources'/app['id']
    if destination.exists():
        raise FileExistsError('Preserve existing source tree: '+str(destination))
    with zipfile.ZipFile(archive_path) as archive:
        for member in archive.infolist():
            relative=Path(member.filename.split('/',1)[-1])
            if not relative.parts:continue
            target=(destination/relative).resolve()
            if not target.is_relative_to(destination.resolve()):
                raise ValueError('Archive member escaped source root')
            if member.is_dir():
                target.mkdir(parents=True,exist_ok=True)
            else:
                target.parent.mkdir(parents=True,exist_ok=True)
                with archive.open(member) as source,target.open('xb') as output:
                    import shutil
                    shutil.copyfileobj(source,output)
    print(json.dumps(dict(app=app['id'],sourceCommit=app['sourceCommit'],directory=str(destination))))
