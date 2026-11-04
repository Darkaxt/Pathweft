"""Pin inert format fixtures from the fallback's own exact upstream revision."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request
import urllib.parse
import re

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'.local/evidence/2026-11-04-specialists/inputs'
REVISION='08dfbf04dcac895fc3a8d30dbf5a00de1b9dda5e'
parser=argparse.ArgumentParser()
parser.add_argument('--directory',action='append',default=[])
parser.add_argument('--file',action='append',default=[])
parser.add_argument('--match',default='.*')
parser.add_argument('--limit',type=int,default=25)
args=parser.parse_args()

def read(url):
    request=urllib.request.Request(url,headers={'User-Agent':'Pathweft-private-fixture-qualification'})
    with urllib.request.urlopen(request) as response:data=response.read(32*1024*1024+1)
    if len(data)>32*1024*1024:raise ValueError('Fixture response exceeds limit')
    return data

for directory in args.directory:
    url='https://api.github.com/repos/LibreOffice/core/contents/'+urllib.parse.quote(directory,safe='/')+'?ref='+REVISION
    items=json.loads(read(url))
    record=dict(directory=directory,url=url,items=[dict(name=x['name'],type=x['type'],bytes=x.get('size'),sha=x['sha']) for x in items])
    index=OUT.parent/'office-fixture-indices';index.mkdir(exist_ok=True)
    (index/(directory.replace('/','_')+'.json')).write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
    selected=[x for x in record['items'] if re.search(args.match,x['name'],re.I)]
    print(json.dumps(dict(directory=directory,total=len(items),matches=len(selected),items=selected[:args.limit]),ensure_ascii=True),flush=True)
manifest_path=OUT/'office-fallback-manifest.json'
manifest=json.loads(manifest_path.read_text()) if manifest_path.exists() else []
for path in args.file:
    url='https://api.github.com/repos/LibreOffice/core/contents/'+urllib.parse.quote(path,safe='/')+'?ref='+REVISION
    info=json.loads(read(url))
    target=OUT/('fallback-'+Path(path).name)
    if not target.exists():target.write_bytes(read(info['download_url']))
    data=target.read_bytes()
    blob=hashlib.sha1(f'blob {len(data)}\0'.encode()+data).hexdigest()
    if blob!=info['sha']:raise ValueError('Pinned Git fixture object differs')
    entry=dict(name=target.name,url=info['download_url'],commit=REVISION,gitBlob=blob,sha256=hashlib.sha256(data).hexdigest(),bytes=len(data))
    manifest=[x for x in manifest if x['name']!=target.name]+[entry]
    print(json.dumps(entry),flush=True)
if args.file:manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
