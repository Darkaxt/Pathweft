"""Prepare each renderer's exact release-pinned font source input and notices."""
import hashlib
import json
import re
import shutil
import zipfile
import importlib.util
import argparse
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('craft_dependencies',ROOT/'scripts/Prepare-CraftOfficeDependencies.py')
helper=importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
parser=argparse.ArgumentParser()
parser.add_argument('--scratch-root',type=Path,default=Path('D:/Temp/pathweft-s13-s9-s11/craft-cores'))
parser.add_argument('--manifest',type=Path,default=ROOT/'vendor/Office/runtime-manifest.json')
parser.add_argument('--engine',action='append')
args=parser.parse_args()
manifest_path=args.manifest
manifest=json.loads(manifest_path.read_text())
scratch=args.scratch_root
for app in manifest['apps']:
    if app['id'] not in (args.engine or ('wordcraft','deckcraft')):continue
    donor=scratch/'sources'/app['id']
    workflow=(donor/'.github/workflows/release.yml').read_text()
    revision=re.search(r'craft-fonts-ref:\s*([0-9a-f]{40})',workflow).group(1)
    url='https://codeload.github.com/storytold/craft-fonts/zip/'+revision
    archive=Path('D:/BuildCache/Pathweft/office/sources')/f'craft-fonts-{revision}.zip'
    expected=app.get('fonts',{}).get('archiveSha256')
    content=helper.obtain(url,archive,expected)
    destination=scratch/'fonts'/app['id']
    if destination.exists():raise FileExistsError('Preserve the existing pinned font source tree')
    notices=[]
    with zipfile.ZipFile(archive) as source:
        for member in source.infolist():
            relative=Path(member.filename.split('/',1)[-1])
            if not relative.parts:continue
            target=(destination/relative).resolve()
            if not target.is_relative_to(destination.resolve()):raise ValueError('Font archive path escapes its source root')
            if member.is_dir():target.mkdir(parents=True,exist_ok=True);continue
            target.parent.mkdir(parents=True,exist_ok=True)
            with source.open(member) as stream,target.open('xb') as output:shutil.copyfileobj(stream,output)
            if relative.name.upper().startswith(('LICENSE','OFL','NOTICE','ATTRIBUTION')):
                notice=ROOT/'vendor/Office/licenses'/app['id']/'craft-fonts'/relative
                notice.parent.mkdir(parents=True,exist_ok=True)
                if notice.exists() and notice.read_bytes()!=target.read_bytes():raise ValueError('Existing font notice differs')
                shutil.copyfile(target,notice)
                notices.append(dict(path=notice.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(notice.read_bytes()).hexdigest()))
    app['fonts']=dict(sourceCommit=revision,archive=archive.name,archiveUrl=url,archiveSha256=hashlib.sha256(content).hexdigest(),notices=notices)
    print(json.dumps(dict(engine=app['id'],fonts=revision,sourceArchiveBytes=len(content),notices=len(notices))))
manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
