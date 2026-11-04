"""Retain official corresponding source for the exact bundled fallback release."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
cache=Path('D:/BuildCache/Pathweft/office/libreoffice/sources')
cache.mkdir(parents=True,exist_ok=True)
base='https://download.documentfoundation.org/libreoffice/src/26.2.7/'
records=[]
for name in ('libreoffice-26.2.7.3.tar.xz','libreoffice-dictionaries-26.2.7.3.tar.xz'):
    url=base+name
    with urllib.request.urlopen(url+'.mirrorlist') as response:details=response.read()
    checksum=re.search(rb'SHA-256.*?([0-9a-f]{64})',details,re.I|re.S)
    if not checksum:raise ValueError('Official source SHA-256 not found: '+name)
    expected=checksum[1].decode().lower()
    target=cache/name
    if not target.exists():
        with urllib.request.urlopen(url) as response,target.open('xb') as output:shutil.copyfileobj(response,output,1024*1024)
    with target.open('rb') as source:actual=hashlib.file_digest(source,'sha256').hexdigest()
    if actual!=expected:raise ValueError('Official source digest differs: '+name)
    records.append(dict(name=name,url=url,sha256=actual,bytes=target.stat().st_size,metadataUrl=url+'.mirrorlist'))
    print(json.dumps(records[-1]),flush=True)
manifest=ROOT/'vendor/Office/libreoffice-runtime-manifest.json'
data=json.loads(manifest.read_text());data['sources']=records
manifest.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8',newline='\n')
