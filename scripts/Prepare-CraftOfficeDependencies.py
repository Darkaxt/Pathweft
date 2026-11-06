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
import argparse

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

def source_only(args):
    manifest=json.loads(args.manifest.read_text())
    if args.engine:manifest['apps']=[app for app in manifest['apps'] if app['id'] in args.engine]
    for app in manifest['apps']:
        archive=args.archives_root/app['sourceArchive']
        content=archive.read_bytes()
        if digest(content)!=app['sourceSha256']:raise ValueError('Pinned source digest differs: '+str(archive))
        notices=[]
        with zipfile.ZipFile(archive) as source:
            files={member.filename.split('/',1)[-1]:member for member in source.infolist() if not member.is_dir()}
            lock_bytes=source.read(files['Cargo.lock'])
            cargo=tomllib.loads(source.read(files['Cargo.toml']).decode())
            for relative,member in files.items():
                path=Path(relative)
                root_notice='/' not in relative
                # The preview uses the donor identification icon and, for
                # VectorCraft, its original bundled fallback fonts as well as
                # craft-fonts. Retain their actual nested licence texts too.
                asset_notice=relative.startswith(('assets/fonts/','assets/app-icon/'))
                if not (root_notice or asset_notice) or not path.name.upper().startswith(('LICENSE','NOTICE','ATTRIBUTION','ASSETS','OFL')):continue
                if path.is_absolute() or '..' in path.parts:raise ValueError('Unsafe source notice path')
                if member.file_size>1<<20:raise ValueError('Unexpected oversized source notice')
                target=VENDOR/'licenses'/app['id']/relative
                target.parent.mkdir(parents=True,exist_ok=True)
                notice=source.read(member);target.write_bytes(notice)
                notices.append(dict(path=target.relative_to(PROJECT).as_posix(),sha256=digest(notice)))
            graph=dict(sourceCommit=app['sourceCommit'],lockSha256=digest(lock_bytes),
                scope='Complete pinned upstream lock, including optional/non-Windows crates; actual core graph is recorded separately.',
                packages=tomllib.loads(lock_bytes.decode())['package'],workspace=cargo.get('workspace',{}))
        (VENDOR/(app['id']+'-source-dependencies.json')).write_text(json.dumps(graph,indent=2)+'\n',encoding='utf-8',newline='\n')
        app.update(notices=notices,license='MIT OR Apache-2.0')
        print(json.dumps(dict(app=app['id'],sourceBytes=len(content),notices=len(notices))),flush=True)
    args.output.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--sources-only',action='store_true')
    parser.add_argument('--manifest',type=Path,default=VENDOR/'runtime-manifest.json')
    parser.add_argument('--output',type=Path)
    parser.add_argument('--archives-root',type=Path,default=CACHE/'sources')
    parser.add_argument('--engine',action='append')
    args=parser.parse_args()
    if args.sources_only:
        if args.output is None:raise ValueError('Source-only preparation requires an explicit output manifest')
        source_only(args);return
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
