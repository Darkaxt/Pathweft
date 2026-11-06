"""Retain the selected core build sources/notices, without rebuilding or restoring."""
import hashlib
import json
from pathlib import Path
import re
import tarfile
import urllib.parse
import urllib.request
import zipfile

ROOT=Path(__file__).resolve().parents[1]
cache=Path('D:/BuildCache/Pathweft')
destination=ROOT/'vendor/Office/licenses/rust'
records={}

def notice(name):
    return Path(name).name.lower().startswith(('license','copying','notice','copyright'))

def save(folder,name,data):
    target=folder/Path(name)
    if not target.resolve().is_relative_to(folder.resolve()):raise ValueError('Notice path escaped its component')
    target.parent.mkdir(parents=True,exist_ok=True)
    target.write_bytes(data)

manifest=json.loads((ROOT/'vendor/Office/runtime-manifest.json').read_text())
for engine in (app['id'] for app in manifest['apps'] if app.get('nativeFiles')):
    graph=json.loads((ROOT/f'vendor/Office/{engine}-core-dependencies.json').read_text())
    for package in graph['packages']:
        source=package.get('source','')
        if source.startswith('registry+'):
            name=package['name']+'-'+package['version']+'.crate'
            matches=list((cache/'cargo/registry/cache').glob('*/'+name))
            if not matches:raise FileNotFoundError('Selected crate source missing: '+name)
            archive=matches[0]
            actual=hashlib.sha256(archive.read_bytes()).hexdigest()
            if actual!=package['checksum']:raise ValueError('Selected crate digest differs: '+name)
            records[name]=dict(name=name,cache=archive.relative_to(cache).as_posix(),sha256=actual,bytes=archive.stat().st_size,
                url='https://crates.io/api/v1/crates/'+package['name']+'/'+package['version']+'/download')
            with tarfile.open(archive,'r:gz') as content:
                for member in content:
                    if member.isfile() and notice(member.name):
                        if member.size>1<<20:raise ValueError('Unexpected oversized notice')
                        relative=member.name.split('/',1)[-1]
                        save(destination/name[:-6],relative,content.extractfile(member).read())
        elif source.startswith('git+'):
            url=urllib.parse.urlsplit(source[4:])
            revision=url.fragment
            if url.hostname!='github.com' or not re.fullmatch('[0-9a-f]{40}',revision):raise ValueError('Unresolved selected Git source: '+source)
            repo=url.path.strip('/').removesuffix('.git')
            name=repo.replace('/','-')+'-'+revision+'.zip'
            archive=cache/'office/sources'/name
            public='https://codeload.github.com/'+repo+'/zip/'+revision
            if not archive.exists():
                with urllib.request.urlopen(public) as response:archive.write_bytes(response.read())
            records[name]=dict(name=name,cache=archive.relative_to(cache).as_posix(),sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),bytes=archive.stat().st_size,url=public)
            with zipfile.ZipFile(archive) as content:
                for member in content.infolist():
                    if not member.is_dir() and notice(member.filename):
                        if member.file_size>1<<20:raise ValueError('Unexpected oversized notice')
                        save(destination/name[:-4],member.filename.split('/',1)[-1],content.read(member))
report=dict(scope='Exact feature/Windows-target selected normal and build dependency source archives; native bridge and donor workspace sources are listed separately in runtime-manifest.json',
    archives=sorted(records.values(),key=lambda x:x['name']))
(ROOT/'vendor/Office/core-source-distribution.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
all_notices=[]
for path in sorted((ROOT/'vendor/Office/licenses').rglob('*')):
    if path.is_file():all_notices.append(dict(path=path.relative_to(ROOT/'vendor/Office').as_posix(),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),bytes=path.stat().st_size))
(ROOT/'vendor/Office/license-manifest.json').write_text(json.dumps(dict(files=all_notices),indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(sourceArchives=len(records),bytes=sum(r['bytes'] for r in records.values()),notices=len(all_notices))))
