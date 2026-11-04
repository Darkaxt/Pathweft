"""Prepare historical CLI comparison inputs, full notices and source/lock inventories.

Persistent D: cache is an active development/release dependency. No GUI/MCP,
native process, installer, preferences or selected Craft installation is changed.
"""
import hashlib
import json
from pathlib import Path
import tomllib
import urllib.request
import zipfile

PROJECT=Path(__file__).resolve().parents[1]
VENDOR=PROJECT/'vendor/Office'
CACHE=Path('D:/BuildCache/Pathweft/office')

def digest(data):
    return hashlib.sha256(data).hexdigest()

def obtain(url,path,expected=None):
    path.parent.mkdir(parents=True,exist_ok=True)
    if not path.exists():
        with urllib.request.urlopen(url) as response:
            content=response.read()
        if expected and digest(content)!=expected:
            raise ValueError('Official pinned artifact hash differs: '+url)
        path.write_bytes(content)
    content=path.read_bytes()
    if expected and digest(content)!=expected:
        raise ValueError('Retained artifact hash differs: '+str(path))
    return content

def main():
    manifest_path=VENDOR/'runtime-manifest.json'
    manifest=json.loads(manifest_path.read_text())
    for app in manifest['apps']:
        name,version=app['id'],app['version']
        archive=f'{name}-{version}-windows-x64-portable.zip'
        url=f'https://github.com/storytold/{name}/releases/download/v{version}/{archive}'
        binary_path=CACHE/'archives'/archive
        obtain(url,binary_path,app['archiveSha256'])
        notice_files=[]
        with zipfile.ZipFile(binary_path) as release:
            cli=[m for m in release.infolist() if Path(m.filename).name==name+'-cli.exe']
            if len(cli)!=1:raise ValueError('Expected one exact CLI in official release')
            content=release.read(cli[0])
            if len(content)!=app['cliBytes'] or digest(content)!=app['cliSha256']:raise ValueError('Official CLI identity differs')
            runtime=CACHE/name/(name+'-cli.exe')
            runtime.parent.mkdir(parents=True,exist_ok=True)
            runtime.write_bytes(content)
            for member in release.infolist():
                leaf=Path(member.filename).name
                if member.is_dir() or not (leaf.startswith(('LICENSE','OFL-','NOTICE','ATTRIBUTION','ASSETS'))):continue
                content=release.read(member)
                target=VENDOR/'licenses'/name/leaf
                if target.exists() and target.read_bytes()!=content:raise ValueError('Reviewed vendor notice changed: '+leaf)
                target.parent.mkdir(parents=True,exist_ok=True)
                target.write_bytes(content)
                notice_files.append(dict(path=target.relative_to(PROJECT).as_posix(),sha256=digest(content)))
        source_url=f'https://codeload.github.com/storytold/{name}/zip/{app["sourceCommit"]}'
        source_path=CACHE/'sources'/f'{name}-{app["sourceCommit"]}.zip'
        source=obtain(source_url,source_path,app.get('sourceSha256'))
        with zipfile.ZipFile(source_path) as archive_source:
            def read(relative):
                files=[m for m in archive_source.infolist() if m.filename.split('/',1)[-1]==relative]
                if len(files)!=1:raise ValueError('Missing exact source record: '+relative)
                return archive_source.read(files[0])
            lock_bytes=read('Cargo.lock')
            lock=tomllib.loads(lock_bytes.decode())
            cargo=tomllib.loads(read('Cargo.toml').decode())
            graph=dict(sourceCommit=app['sourceCommit'],lockSha256=digest(lock_bytes),
                scope='Complete pinned upstream lock; includes optional and non-Windows packages, not a claim that every member is linked into the CLI',
                packages=lock['package'],workspace=cargo.get('workspace',{}))
            (VENDOR/(name+'-source-dependencies.json')).write_text(json.dumps(graph,indent=2)+'\n',encoding='utf-8',newline='\n')
        app.update(archive=archive,archiveUrl=url,sourceArchive=source_path.name,sourceUrl=source_url,
            sourceSha256=digest(source),notices=notice_files,license='MIT OR Apache-2.0')
        print(json.dumps(dict(app=name,cliBytes=app['cliBytes'],sourceBytes=len(source),lockPackages=len(lock['package']),notices=len(notice_files))),flush=True)
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')

if __name__=='__main__':main()
