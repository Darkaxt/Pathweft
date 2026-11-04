"""Record the official fallback's source recipes and selected PE import boundary.

No native library is loaded and no dependency version is inferred from a filename.
The upstream download table is a build universe, not an asserted linkage graph.
"""
import hashlib
import json
from pathlib import Path
import re
import tarfile
import pefile

ROOT=Path(__file__).resolve().parents[1]
cache=Path('D:/BuildCache/Pathweft/office/libreoffice')
manifest=json.loads((ROOT/'vendor/Office/libreoffice-runtime-manifest.json').read_text())
source=cache/'sources'/next(x['name'] for x in manifest['sources'] if x['name'].startswith('libreoffice-26.'))
selected={}
with tarfile.open(source,'r|xz') as archive:
    for item in archive:
        relative=item.name.split('/',1)[-1]
        if item.isfile() and (relative in ('download.lst','distro-configs/LibreOfficeWin64.conf','RepositoryExternal.mk','README.md') or relative.startswith('external/') and relative.endswith(('.mk','.patch','.patch.1','.patch.2'))):
            selected[relative]=archive.extractfile(item).read()
table=selected['download.lst'].decode()
values={}
for name,value in re.findall(r'^\s*(?:export\s+)?([A-Z0-9_]+)\s*:?=\s*([^\n#]+)',table,re.M):values[name]=value.strip()
for _ in range(4):
    values={name:re.sub(r'\$\(([A-Z0-9_]+)\)',lambda match:values.get(match[1],match[0]),value) for name,value in values.items()}
records=[]
for name,value in values.items():
    if not name.endswith('_TARBALL'):continue
    prefix=name[:-8]
    records.append(dict(id=prefix.lower(),archive=value,sha256=values.get(prefix+'_SHA256SUM'),version=values.get(prefix+'_VERSION')))
imports=[]
for file in manifest['files']:
    if Path(file['path']).suffix.lower() not in ('.dll','.exe','.bin','.pyd'):continue
    path=cache/'runtime'/file['path']
    with path.open('rb') as stream:
        if stream.read(2)!=b'MZ':continue
    with pefile.PE(str(path),fast_load=True) as pe:
        pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT'],pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT']])
        names=sorted({entry.dll.decode('ascii') for directory in ('DIRECTORY_ENTRY_IMPORT','DIRECTORY_ENTRY_DELAY_IMPORT') for entry in getattr(pe,directory,[])})
        imports.append(dict(path=file['path'],sha256=file['sha256'],imports=names))
report=dict(version=manifest['sourceVersion'],sourceArchive=source.name,
    scope='Exact distributed PE imports plus official external build source table. Static linkage is described by the shipped upstream license; the download table includes unused/platform/build-only components and is not a linked SBOM.',
    externalSources=records,peImports=imports,recipes=[dict(path=path,sha256=hashlib.sha256(data).hexdigest()) for path,data in sorted(selected.items())])
output=ROOT/'vendor/Office/libreoffice-dependencies.json'
output.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
evidence=ROOT/'.local/evidence/2026-11-04-specialists/libreoffice-source-recipes'
for path,data in selected.items():
    target=evidence/path;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
print(json.dumps(dict(peFiles=len(imports),externalBuildSources=len(records),missingHashes=[r for r in records if not r['sha256']],manifest=str(output))))
