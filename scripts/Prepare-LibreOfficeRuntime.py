"""Fetch a pinned official MSI as data, without installing or launching Office.

The hash comes from the retained official MirrorBrain metadata. The exact
download/extraction root is registered for transactional cleanup.
"""
import argparse
import hashlib
import json
import shutil
import subprocess
import urllib.request
from pathlib import Path

VERSION='26.2.7'
SHA256='480fecf759fc6393bd614fbf43753eb95a8198fc1a065d41b727e3b664cd1e59'
NAME=f'LibreOffice_{VERSION}_Win_x86-64.msi'
URL=f'https://download.documentfoundation.org/libreoffice/stable/{VERSION}/win/x86_64/{NAME}'
ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--scratch-root',type=Path,required=True)
args=parser.parse_args()
scratch=args.scratch_root.resolve()
if str(scratch).lower()!='d:\\temp\\pathweft-s13-s9-s11\\office-fallback':
    raise ValueError('Use the registered exact LibreOffice scratch root')
scratch.mkdir(parents=True,exist_ok=True)
archive=scratch/NAME
cache=Path('D:/BuildCache/Pathweft/office/libreoffice/archives')/NAME
if cache.exists():
    archive=cache
else:
    if archive.exists():raise FileExistsError('Inspect the previous download before another attempt')
    with urllib.request.urlopen(URL) as response,archive.open('xb') as output:
        shutil.copyfileobj(response,output,1024*1024)
with archive.open('rb') as source:
    actual=hashlib.file_digest(source,'sha256').hexdigest()
if actual!=SHA256:raise ValueError(f'LibreOffice MSI hash mismatch: {actual}')
if archive!=cache:
    cache.parent.mkdir(parents=True,exist_ok=True)
    shutil.move(archive,cache)
report=scratch/'msi-members.txt'
with report.open('wb') as output:
    subprocess.run(['C:/Users/darka/scoop/shims/7z.exe','l','-slt',str(cache)],stdout=output,check=True)
evidence=ROOT/'.local/evidence/2026-11-04-specialists/libreoffice-msi.json'
evidence.write_text(json.dumps(dict(version=VERSION,url=URL,sha256=actual,bytes=cache.stat().st_size,archive=str(cache),members=str(report)),indent=2)+'\n')
print(evidence.read_text())
