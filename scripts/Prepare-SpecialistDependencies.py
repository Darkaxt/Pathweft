"""Prepare exact specialist source/license inputs; no install, build or live changes.

Persistent D: dependency cache supports repeatable builds and corresponding-source
delivery. Official source archives are retained whole; only license/notice text
is copied into the application vendor inventory. Uncertain provenance stays explicit.
"""
import hashlib
import io
import json
from pathlib import Path
import urllib.request
import zipfile

PROJECT=Path(__file__).resolve().parents[1]
CACHE=Path('D:/BuildCache/Pathweft/specialists/s13')

def main():
    pins=json.loads((PROJECT/'vendor/Specialists/source-pins.json').read_text())
    manifest_path=PROJECT/'vendor/Specialists/source-manifest.json'
    prior={p['id']:p for p in json.loads(manifest_path.read_text())} if manifest_path.exists() else {}
    CACHE.mkdir(parents=True,exist_ok=True)
    records=[]
    for pin in pins:
        name=pin['repo'].replace('/','-')+'-'+pin['revision']+'.zip'
        url='https://codeload.github.com/'+pin['repo']+'/zip/'+pin['revision']
        target=CACHE/name
        if not target.exists():
            with urllib.request.urlopen(url) as response:
                data=response.read(128*1024*1024+1)
            if len(data)>128*1024*1024: raise ValueError('Source archive exceeds preparation budget')
            target.write_bytes(data)
        digest=hashlib.sha256(target.read_bytes()).hexdigest()
        expected=prior.get(pin['id'],{})
        if expected and (expected['revision']!=pin['revision'] or expected['sourceSha256']!=digest): raise ValueError('Reviewed source pin changed: '+name)
        notices=[]
        with zipfile.ZipFile(target) as archive:
            for member in archive.infolist():
                relative=member.filename.split('/',1)[-1]
                leaf=Path(relative).name.lower()
                if member.is_dir() or leaf not in ('license','license.txt','license.md','license-apache-2.0.txt','license-ms-pl.txt','notice','notice.txt','copying','copying.txt') or relative.count('/')>1: continue
                if member.file_size>1024*1024: raise ValueError('Oversized source notice')
                output=PROJECT/'vendor/Specialists/licenses'/pin['id']/(relative.replace('/','-')+'.txt')
                output.parent.mkdir(parents=True,exist_ok=True)
                content=archive.read(member)
                output.write_bytes(content)
                notices.append(dict(path=output.relative_to(PROJECT).as_posix(),sha256=hashlib.sha256(content).hexdigest()))
        if not notices and pin['id']=='QuickLook.ApkReader':
            # The pinned source has no license file. Its actual NuGet nuspec
            # explicitly declares MIT; preserve that declaration and its authors.
            with urllib.request.urlopen('https://licenses.nuget.org/MIT') as response: license_text=response.read()
            output=PROJECT/'vendor/Specialists/licenses'/pin['id']/'NuGet-MIT.txt'
            output.parent.mkdir(parents=True,exist_ok=True)
            content=b'QuickLook.ApkReader2.1.0; authors Soar360 QL-Win.\nLicense declared by the shipped NuGet metadata: MIT.\nhttps://licenses.nuget.org/MIT\n\n'+license_text
            output.write_bytes(content)
            notices.append(dict(path=output.relative_to(PROJECT).as_posix(),sha256=hashlib.sha256(content).hexdigest()))
        if not notices: raise ValueError('No source license found: '+name)
        package=Path('D:/BuildCache/Pathweft/nuget-packages')/pin['id'].lower()/pin['version']/(pin['id'].lower()+'.'+pin['version']+'.nupkg')
        if not package.is_file(): raise ValueError('Actual selected package is missing: '+str(package))
        package_target=CACHE/package.name
        data=package.read_bytes()
        if expected and expected['packageSha256']!=hashlib.sha256(data).hexdigest(): raise ValueError('Reviewed package changed: '+pin['id'])
        if package_target.exists() and package_target.read_bytes()!=data: raise ValueError('Retained package changed')
        package_target.write_bytes(data)
        records.append(dict(**pin,sourceArchive=name,sourceUrl=url,sourceSha256=digest,packageArchive=package.name,packageSha256=hashlib.sha256(data).hexdigest(),notices=notices))
        print(pin['id']+' '+pin['version']+' source/licenses retained',flush=True)
    manifest=json.dumps(records,indent=2)+'\n'
    (PROJECT/'vendor/Specialists/source-manifest.json').write_text(manifest,encoding='utf-8')
    (CACHE/'source-manifest.json').write_text(manifest,encoding='utf-8')

if __name__=='__main__': main()
