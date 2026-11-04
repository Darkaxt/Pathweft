"""Stage built, pinned Pathweft libraries and verified fallback data; never install apps."""
import hashlib
import json
from pathlib import Path
import shutil
import pefile

ROOT=Path(__file__).resolve().parents[1]
scratch=Path('D:/Temp/pathweft-s13-s9-s11')
cache=Path('D:/BuildCache/Pathweft/office')
manifest_path=ROOT/'vendor/Office/runtime-manifest.json'
manifest=json.loads(manifest_path.read_text())
fallback=json.loads((ROOT/'vendor/Office/libreoffice-runtime-manifest.json').read_text())
payload=scratch/'office-fallback/payload'
runtime=cache/'libreoffice/runtime'

def digest(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()

def copy_checked(source,target,expected=None):
    checksum=digest(source)
    if expected and checksum!=expected:raise ValueError('Changed input: '+str(source))
    target.parent.mkdir(parents=True,exist_ok=True)
    if target.exists():
        if digest(target)!=checksum:raise FileExistsError('Preserve existing runtime: '+str(target))
    else:shutil.copyfile(source,target)
    if digest(target)!=checksum:raise ValueError('Copy differs: '+str(target))
    return dict(path=target.relative_to(cache/'cores').as_posix(),bytes=target.stat().st_size,sha256=checksum)

pins={'wordcraft':'7655c880347b6c3e250e9f0364ffe372641a09d565fe971dc52945b08c3bdbd8',
      'gridcraft':'42345101ce2e43a4c6c90cb2e3bcfe001da482876509657e1f516f5d7a32ed7e',
      'deckcraft':'3bc79ac167c49b453395bb3890400e1cf0646ced60ee1b3f3f503aed485e5f0d'}
for app in manifest['apps']:
    name='pathweft_'+app['id']+'.dll'
    source=scratch/'craft-cores/target/native/release'/name
    target=cache/'cores'/app['id']/name
    records=[copy_checked(source,target,pins[app['id']])]
    with pefile.PE(str(source),fast_load=True) as pe:
        pe.parse_data_directories(directories=[1,13])
        imports=sorted({item.dll.decode('ascii') for field in ('DIRECTORY_ENTRY_IMPORT','DIRECTORY_ENTRY_DELAY_IMPORT') for item in getattr(pe,field,[])})
    # App-local Microsoft redistributables from the official fallback MSI;
    # other imports are Windows API contracts/system DLLs, recorded explicitly.
    for dependency in imports:
        if dependency.lower().startswith(('vcruntime','msvcp','concrt')):
            records.append(copy_checked((payload if payload.exists() else runtime)/'program'/dependency,cache/'cores'/app['id']/dependency))
    app.update(nativeRuntime='Office/cores/'+app['id']+'/'+name,nativeSha256=records[0]['sha256'],
        nativeBytes=records[0]['bytes'],nativeFiles=records,nativeImports=imports,
        abi=1,buildToolchain='Rust 1.95.0 x86_64-pc-windows-msvc; one compiler job',
        baselineCli='Historical comparison only; no CLI executable ships or runs')
manifest['fallback']='libreoffice-runtime-manifest.json'
manifest['architecture']='Pinned source-core DLLs in a Pathweft-owned worker; bundled LibreOffice read-only fallback'
manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
source_root=payload if payload.exists() else runtime
for file in fallback['files']:
    if digest(source_root/file['path'])!=file['sha256']:raise ValueError('Fallback input changed: '+file['path'])
if payload.exists():
    if runtime.exists():raise FileExistsError('Preserve existing adopted fallback')
    # Same-volume adoption of a verified task-created payload; archives stay in
    # their separate cache directory and cannot enter the runtime item glob.
    payload.rename(runtime)
print(json.dumps(dict(cores=[dict(id=a['id'],files=a['nativeFiles'],imports=a['nativeImports']) for a in manifest['apps']],fallbackFiles=len(fallback['files']),fallbackBytes=sum(f['bytes'] for f in fallback['files']))))
