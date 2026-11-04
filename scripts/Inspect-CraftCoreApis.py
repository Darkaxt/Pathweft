"""Inspect pinned source archives without executing or changing upstream code."""
import hashlib
import json
import re
import zipfile
import argparse
import sys
sys.stdout.reconfigure(encoding='utf-8')
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
EVIDENCE=ROOT/'.local/evidence/2026-11-04-specialists'
CACHE=Path('D:/BuildCache/Pathweft/office/sources')
manifest=json.loads((ROOT/'vendor/Office/runtime-manifest.json').read_text())
parser=argparse.ArgumentParser()
parser.add_argument('--app')
parser.add_argument('--file',action='append',default=[])
parser.add_argument('--start',type=int,default=1)
parser.add_argument('--count',type=int,default=240)
parser.add_argument('--pattern')
args=parser.parse_args()
if args.file:
    app=next(a for a in manifest['apps'] if a['id']==args.app)
    path=CACHE/app['sourceArchive']
    if hashlib.sha256(path.read_bytes()).hexdigest()!=app['sourceSha256']:
        raise ValueError('Pinned donor archive differs: '+app['id'])
    with zipfile.ZipFile(path) as archive:
        for requested in args.file:
            matches=[n for n in archive.namelist() if n.split('/',1)[-1]==requested]
            if len(matches)!=1:raise ValueError('Expected one source file: '+requested)
            lines=archive.read(matches[0]).decode('utf-8').splitlines()
            print(requested)
            indices=range(args.start-1,min(len(lines),args.start-1+args.count))
            if args.pattern:
                selected=set()
                for i,line in enumerate(lines):
                    if re.search(args.pattern,line):selected.update(range(max(0,i-2),min(len(lines),i+12)))
                indices=sorted(selected)
            for i in indices:
                print(f'{i+1}: {lines[i]}')
    raise SystemExit(0)
results=[]
for app in manifest['apps']:
    path=CACHE/app['sourceArchive']
    if hashlib.sha256(path.read_bytes()).hexdigest()!=app['sourceSha256']:
        raise ValueError('Pinned donor archive differs: '+app['id'])
    files=[]
    with zipfile.ZipFile(path) as archive:
        for member in archive.infolist():
            relative=member.filename.split('/',1)[-1]
            if not relative.startswith('crates/') or not relative.endswith('.rs'):
                continue
            parts=relative.split('/')
            if parts[1] not in {'render','layout','doc','model','fonts','text','xlsx','docx','pptx','engine','chart'}:
                continue
            if '/tests/' in relative or '/commands/' in relative:
                continue
            source=archive.read(member).decode('utf-8')
            lines=source.splitlines()
            matches=[dict(line=i+1,text=line.strip()) for i,line in enumerate(lines)
                     if re.search(r'pub (?:async )?fn |pub struct |pub enum |pub use ',line)
                     and re.search(r'render|paint|load|open|parse|layout|page|sheet|slide|font|draw|tile|viewport|raster|width|height',line,re.I)]
            if matches:files.append(dict(path=relative,matches=matches))
    results.append(dict(app=app['id'],sourceCommit=app['sourceCommit'],files=files))
EVIDENCE.mkdir(parents=True,exist_ok=True)
(EVIDENCE/'craft-core-apis.json').write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')
for result in results:
    print(json.dumps(dict(app=result['app'],files=[f for f in result['files'] if f['path'].endswith('/lib.rs') or 'render/src' in f['path']]),ensure_ascii=False))
