"""Retain a bounded, pinned PSD/PSB oracle subset for actual core qualification."""
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
donor=Path('D:/Temp/pathweft-s16/craft-cores/sources/photocraft')
target=ROOT/'.local/evidence/2026-11-06-s16/photo-inputs'
target.mkdir(parents=True,exist_ok=True)
revision='96eb134c17b2c65edf4c4151c0f00b802ada86c2'
pins={line.split('  ',1)[1]:line.split('  ',1)[0] for line in (donor/'xtask/psd-tools-corpus.sha256').read_text().splitlines() if '  ' in line}
records=[]
for name in ('2layers.psd','2layers.psb','text.psd','16bit5x5.psb'):
    url=f'https://raw.githubusercontent.com/psd-tools/psd-tools/{revision}/tests/psd_files/{name}'
    path=target/('oracle-'+name)
    if not path.exists():
        with urllib.request.urlopen(url) as source:content=source.read()
        if hashlib.sha256(content).hexdigest()!=pins[name]:raise ValueError('Pinned PSD oracle differs: '+name)
        path.write_bytes(content)
    if hashlib.sha256(path.read_bytes()).hexdigest()!=pins[name]:raise ValueError('Retained oracle differs: '+name)
    records.append(dict(path=path.name,url=url,sha256=pins[name],bytes=path.stat().st_size))
url=f'https://raw.githubusercontent.com/psd-tools/psd-tools/{revision}/LICENSE'
license_path=target/'psd-tools-LICENSE'
if not license_path.exists():
    with urllib.request.urlopen(url) as source:license_path.write_bytes(source.read())
records.append(dict(path=license_path.name,url=url,sha256=hashlib.sha256(license_path.read_bytes()).hexdigest()))
(target/'manifest.json').write_text(json.dumps(dict(sourceCommit=revision,files=records),indent=2)+'\n')
print(json.dumps(dict(directory=str(target),files=len(records))))
