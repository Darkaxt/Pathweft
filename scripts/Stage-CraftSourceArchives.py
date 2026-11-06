"""Retain exact donor archives as active build/corresponding-source inputs."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

parser=argparse.ArgumentParser()
parser.add_argument('--manifest',type=Path,required=True)
parser.add_argument('--source-root',type=Path,required=True)
parser.add_argument('--cache-root',type=Path,default=Path('D:/BuildCache/Pathweft/office/sources'))
args=parser.parse_args()
args.cache_root.mkdir(parents=True,exist_ok=True)
def digest(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
records=[]
for app in json.loads(args.manifest.read_text())['apps']:
    name=app['sourceArchive']
    if Path(name).name!=name:raise ValueError('Source archive name must be a leaf')
    source=args.source_root/name;target=args.cache_root/name;expected=app['sourceSha256']
    if digest(source)!=expected:raise ValueError('Source archive changed: '+str(source))
    if target.exists():
        if digest(target)!=expected:raise FileExistsError('Preserve the different retained source: '+str(target))
    else:shutil.copyfile(source,target)
    if digest(target)!=expected:raise ValueError('Retained source copy differs: '+str(target))
    records.append(dict(id=app['id'],sourceCommit=app['sourceCommit'],archive=str(target),sha256=expected,bytes=target.stat().st_size))
print(json.dumps(records,indent=2))
