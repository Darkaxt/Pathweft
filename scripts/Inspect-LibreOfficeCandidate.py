"""Retrieve official candidate metadata only; no installation or native launch."""
import json
import re
import urllib.request
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
EVIDENCE=ROOT/'.local/evidence/2026-11-04-specialists'
VERSION='26.2.7'
URL=f'https://download.documentfoundation.org/libreoffice/stable/{VERSION}/win/x86_64/'
request=urllib.request.Request(URL,headers={'User-Agent':'Pathweft dependency qualification'})
with urllib.request.urlopen(request) as response:
    content=response.read()
    final=response.url
EVIDENCE.mkdir(parents=True,exist_ok=True)
(EVIDENCE/'libreoffice-candidate-index.html').write_bytes(content)
name=f'LibreOffice_{VERSION}_Win_x86-64.msi'
with urllib.request.urlopen(URL+name+'.mirrorlist') as response:
    details=response.read()
(EVIDENCE/'libreoffice-candidate-details.html').write_bytes(details)
with urllib.request.urlopen(f'https://download.documentfoundation.org/libreoffice/src/{VERSION}/') as response:
    sources=response.read()
(EVIDENCE/'libreoffice-candidate-sources.html').write_bytes(sources)
print(json.dumps(dict(version=VERSION,url=final,installer=name,
    hashes=[line.strip() for line in details.decode().splitlines() if 'sha256' in line.lower() or 'sha-256' in line.lower()],
    sources=sorted(set(re.findall(r'href="([^"?]+\.tar\.xz)"',sources.decode()))))))
