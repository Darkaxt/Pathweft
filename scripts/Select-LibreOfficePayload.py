"""Select the read-only headless fallback payload from the official MSI tree.

Keep all native format filters, locale data, fonts and hyphenation dictionaries.
Omit interactive help/templates/gallery, extra UI translations/icon themes, and
spelling/thesaurus data that cannot participate in PDF preview rendering. The
complete extracted candidate remains available for qualified comparisons until
the selected payload has passed the real fallback workflows.
"""
import hashlib
import json
from pathlib import Path
import shutil

ROOT=Path(__file__).resolve().parents[1]
scratch=Path('D:/Temp/pathweft-s13-s9-s11/office-fallback')
source=scratch/'runtime'
target=scratch/'payload'
if target.exists():raise FileExistsError('Preserve the existing selected payload')
target.mkdir()
selected=[]
omitted={}

def exclusion(relative):
    parts=relative.parts
    leaf=relative.name.lower()
    if parts[0]=='help':return 'Interactive help'
    if parts[:2] in [('share','gallery'),('share','template'),('share','autotext'),('share','wizards')]:return 'Interactive authoring assets'
    if parts[:2]==('program','resource') and len(parts)>3 and parts[2] not in ('en-US','en_US'):return 'Additional UI translation'
    if parts[:3]==('share','registry','res') and relative.suffix=='.xcd' and not leaf.endswith('_en-us.xcd'):return 'Additional UI translation'
    if parts[:2]==('share','config') and leaf.startswith('images_') and not leaf.startswith('images_colibre'):return 'Additional UI icon theme'
    if parts[:2]==('share','extensions') and relative.suffix.lower() in ('.dic','.aff','.dat','.idx') and not leaf.startswith('hyph_'):return 'Spelling/thesaurus data; hyphenation retained'
    return None

for path in source.rglob('*'):
    if not path.is_file():continue
    relative=path.relative_to(source)
    reason=exclusion(relative)
    if reason:
        totals=omitted.setdefault(reason,dict(files=0,bytes=0))
        totals['files']+=1;totals['bytes']+=path.stat().st_size
        continue
    output=target/relative
    output.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(path,output)
    with output.open('rb') as stream:checksum=hashlib.file_digest(stream,'sha256').hexdigest()
    selected.append(dict(path=relative.as_posix(),bytes=output.stat().st_size,sha256=checksum))
manifest=dict(status='candidate; selected-payload qualification required',version='26.2.7',sourceVersion='26.2.7.3',
    sourceUrl='https://download.documentfoundation.org/libreoffice/src/26.2.7/libreoffice-26.2.7.3.tar.xz',
    msiUrl='https://download.documentfoundation.org/libreoffice/stable/26.2.7/win/x86_64/LibreOffice_26.2.7_Win_x86-64.msi',
    msiSha256='480fecf759fc6393bd614fbf43753eb95a8198fc1a065d41b727e3b664cd1e59',
    profile='Private en-US UI; system/document content locale retained; OpenCL disabled only for the owned process; read-only, no macro execution or link updates',
    files=selected,omitted=omitted)
(ROOT/'vendor/Office/libreoffice-runtime-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(dict(payload=str(target),files=len(selected),bytes=sum(f['bytes'] for f in selected),omitted=omitted)))
