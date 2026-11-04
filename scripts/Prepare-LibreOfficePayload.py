"""Complete app-local MSI runtime assets and report candidate footprint.

Fonts and the x64 Microsoft runtime are stored outside INSTALLLOCATION in the
official MSI. Place them app-locally instead of installing into Windows folders.
No native module, installer or registry action is executed.
"""
import hashlib
import json
from pathlib import Path
import shutil
from collections import defaultdict

ROOT=Path(__file__).resolve().parents[1]
scratch=Path('D:/Temp/pathweft-s13-s9-s11/office-fallback')
layout=json.loads((scratch/'msi-layout.json').read_text(encoding='utf-8-sig'))
runtime=scratch/'runtime'
members=[]
for identifier,component,name,size in layout['files']:
    leaf=name.split('|')[-1]
    source=scratch/'cab-files'/identifier
    if not source.exists():continue
    if leaf.lower().endswith(('.ttf','.otf')):
        target=runtime/'share/fonts/truetype'/leaf
    elif '_amd64.' in identifier and leaf.lower().endswith('.dll'):
        target=runtime/'program'/leaf
    else:continue
    if source.stat().st_size!=int(size):raise ValueError('MSI source size differs: '+identifier)
    target.parent.mkdir(parents=True,exist_ok=True)
    if target.exists():raise FileExistsError('Preserve an existing app-local runtime file: '+str(target))
    shutil.move(source,target)
    members.append(dict(msiId=identifier,path=target.relative_to(runtime).as_posix(),sha256=hashlib.sha256(target.read_bytes()).hexdigest(),bytes=target.stat().st_size))
sizes=defaultdict(int)
for path in runtime.rglob('*'):
    if path.is_file():
        parts=path.relative_to(runtime).parts
        sizes['/'.join(parts[:min(2,len(parts)-1)]) or '/']+=path.stat().st_size
report=dict(msi='LibreOffice_26.2.7_Win_x86-64.msi',appLocalAdditions=members,
    totalBytes=sum(sizes.values()),directories=sorted(sizes.items(),key=lambda item:item[1],reverse=True))
(ROOT/'.local/evidence/2026-11-04-specialists/libreoffice-payload.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(dict(additions=len(members),bytes=report['totalBytes'],directories=report['directories'][:20])))
