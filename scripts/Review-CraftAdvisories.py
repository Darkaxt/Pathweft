"""Match actual selected Cargo graphs to a pinned official RustSec snapshot.

Candidate matches require used-path review; this is not a whole-product scan.
Unsupported range syntax remains unresolved rather than silently called safe.
"""
import hashlib
import json
from pathlib import Path
import re
import tomllib
import urllib.request
import zipfile

ROOT=Path(__file__).resolve().parents[1]
cache=Path('D:/BuildCache/Pathweft/advisories');cache.mkdir(parents=True,exist_ok=True)
request=urllib.request.Request('https://api.github.com/repos/RustSec/advisory-db/commits/HEAD',headers={'User-Agent':'Pathweft-dependency-review'})
with urllib.request.urlopen(request) as response:revision=json.load(response)['sha']
if not re.fullmatch('[a-f0-9]{40}',revision):raise ValueError('Unresolved official advisory revision')
archive=cache/(revision+'.zip')
if not archive.exists():
    with urllib.request.urlopen('https://codeload.github.com/RustSec/advisory-db/zip/'+revision) as response:archive.write_bytes(response.read())
graphs={path.stem.removesuffix('-core-dependencies'):json.loads(path.read_text()) for path in (ROOT/'vendor/Office').glob('*-core-dependencies.json')}
packages={}
for engine,graph in graphs.items():
    for package in graph['packages']:
        packages.setdefault(package['name'],{}).setdefault(package['version'],[]).append(engine)

def matches(version,requirement):
    if '-' in version:return None
    actual=tuple(map(int,version.split('.')))
    for clause in requirement.split(','):
        match=re.fullmatch(r'\s*(>=|<=|>|<|=)?\s*(\d+\.\d+\.\d+)\s*',clause)
        if not match:return None
        operator=match[1] or '=';expected=tuple(map(int,match[2].split('.')))
        if not {'>=':actual>=expected,'<=':actual<=expected,'>':actual>expected,'<':actual<expected,'=':actual==expected}[operator]:return False
    return True

records=[]
with zipfile.ZipFile(archive) as content:
    for member in content.infolist():
        if not re.search(r'/crates/[^/]+/RUSTSEC-[^/]+\.md$',member.filename):continue
        text=content.read(member).decode();block=re.search(r'```toml\s*\n(.*?)```',text,re.S)
        if not block:continue
        item=tomllib.loads(block[1]);advisory=item['advisory'];name=advisory['package']
        if name not in packages or advisory.get('withdrawn'):continue
        ranges=item.get('versions',{});safe=ranges.get('patched',[])+ranges.get('unaffected',[])
        for version,engines in packages[name].items():
            results=[matches(version,requirement) for requirement in safe]
            state='outside-affected-range' if True in results else 'unresolved-range' if None in results else 'candidate'
            body=text.split('```',2)[-1].strip()
            title=advisory.get('title') or next((line.lstrip('# ').strip() for line in body.splitlines() if line.startswith('# ')),advisory['id'])
            records.append(dict(id=advisory['id'],package=name,version=version,engines=engines,state=state,informational=advisory.get('informational'),
                title=title,url='https://rustsec.org/advisories/'+advisory['id']+'.html',versions=ranges,description=body))
report=dict(database=dict(repository='https://github.com/RustSec/advisory-db',revision=revision,archiveSha256=hashlib.sha256(archive.read_bytes()).hexdigest()),
    scope='Actual selected normal/build Windows-target package graphs; candidate advisories are not applicability conclusions',matches=records)
target=ROOT/'.local/evidence/2026-11-06-s16/advisory-matches.json';target.write_text(json.dumps(report,indent=2,default=str)+'\n',encoding='utf-8')
print(json.dumps(dict(revision=revision,graphs=list(graphs),matches=len(records),candidates=[{k:r[k] for k in ('id','package','version','engines','state','informational','title','versions')} for r in records if r['state']!='outside-affected-range']),indent=2))
