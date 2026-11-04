"""Compare a bridge build's donor lock against the immutable pinned archive."""
import argparse
import hashlib
import json
import tomllib
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--engine',required=True)
args=parser.parse_args()
manifest=json.loads((ROOT/'vendor/Office/runtime-manifest.json').read_text())
app=next(a for a in manifest['apps'] if a['id']==args.engine)
archive=Path('D:/BuildCache/Pathweft/office/sources')/app['sourceArchive']
with archive.open('rb') as stream:
    if hashlib.file_digest(stream,'sha256').hexdigest()!=app['sourceSha256']:raise ValueError('Donor archive hash mismatch')
with zipfile.ZipFile(archive) as source:
    name=next(n for n in source.namelist() if n.split('/',1)[-1]=='Cargo.lock')
    original=tomllib.loads(source.read(name).decode())
current_path=Path('D:/Temp/pathweft-s13-s9-s11/craft-cores/sources')/args.engine/'Cargo.lock'
current=tomllib.loads(current_path.read_text())
def packages(lock):
    return {(p['name'],p['version'],p.get('source','')):p for p in lock['package']}
before=packages(original);after=packages(current)
removed=[key for key in before if key not in after]
changed=[key for key in before if key in after and before[key]!=after[key]]
added=[key for key in after if key not in before]
expected=(f'pathweft-{args.engine}','0.1.0','')
if removed or changed or added!=[expected]:
    raise ValueError(json.dumps(dict(removed=removed,changed=changed,added=added)))
print(json.dumps(dict(engine=args.engine,preserved_upstream_packages=len(before),added_bridge=expected,lockSha256=hashlib.sha256(current_path.read_bytes()).hexdigest())))
