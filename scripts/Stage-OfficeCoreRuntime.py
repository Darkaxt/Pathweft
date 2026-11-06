"""Stage built, pinned Pathweft libraries and verified fallback data; never install apps."""
import hashlib
import json
from pathlib import Path
import shutil
import pefile
import argparse

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--scratch-root',type=Path,default=Path('D:/Temp/pathweft-s13-s9-s11'))
parser.add_argument('--core-root',type=Path,default=Path('D:/BuildCache/Pathweft/office/cores'))
parser.add_argument('--manifest',type=Path,default=ROOT/'vendor/Office/runtime-manifest.json')
parser.add_argument('--cores-only',action='store_true',help='Stage only core DLLs and their manifest-verified redistributables; leave the adopted fallback unchanged')
args=parser.parse_args()
scratch=args.scratch_root
cache=Path('D:/BuildCache/Pathweft/office')
cores=args.core_root
manifest_path=args.manifest
manifest=json.loads(manifest_path.read_text())
fallback=json.loads((ROOT/'vendor/Office/libreoffice-runtime-manifest.json').read_text())
fallback_hashes={file['path'].replace('\\','/').lower():file['sha256'] for file in fallback['files']}
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
    return dict(path=target.relative_to(cores).as_posix(),bytes=target.stat().st_size,sha256=checksum)

pins={'wordcraft':'7655c880347b6c3e250e9f0364ffe372641a09d565fe971dc52945b08c3bdbd8',
      'gridcraft':'42345101ce2e43a4c6c90cb2e3bcfe001da482876509657e1f516f5d7a32ed7e',
      'deckcraft':'3bc79ac167c49b453395bb3890400e1cf0646ced60ee1b3f3f503aed485e5f0d'}
for app in manifest['apps']:
    if app['id'] not in pins and scratch==Path('D:/Temp/pathweft-s13-s9-s11'):continue
    if not (scratch/'craft-cores/receipts'/f"{app['id']}-Build.json").exists() and app['id'] not in pins:continue
    name='pathweft_'+app['id']+'.dll'
    source=scratch/'craft-cores/target/native/release'/name
    target=cores/app['id']/name
    expected=pins.get(app['id'])
    if scratch!=Path('D:/Temp/pathweft-s13-s9-s11'):
        receipt=json.loads((scratch/'craft-cores/receipts'/f"{app['id']}-Build.json").read_text(encoding='utf-8-sig'))
        if receipt['source']!=app['sourceCommit'] or Path(receipt['dll'])!=source:raise ValueError('Build receipt source differs')
        staged=scratch/'craft-cores/sources'/app['id']/'apps/pathweft-office-bridge'
        inputs=[staged/'Cargo.toml',*sorted((staged/'src').glob('*'),key=lambda p:p.name)]
        if ':'.join(digest(p).upper() for p in inputs)!=receipt['inputHashes']:raise ValueError('Bridge inputs differ from completed build')
        current=[(ROOT/'native/office'/app['id']/'Cargo.toml',staged/'Cargo.toml')]
        current.extend((p,staged/'src'/p.name) for folder in (ROOT/'native/office'/app['id']/'src',ROOT/'native/office/common') for p in folder.glob('*.rs'))
        if any(digest(source_input)!=digest(staged_input) for source_input,staged_input in current):raise ValueError('Current source differs from the built bridge')
        expected=receipt['sha256']
        patches=receipt.get('compatibilityPatches',[])
        for patch in patches:
            changed=scratch/'craft-cores/sources'/app['id']/patch['path']
            if digest(changed)!=patch['sha256']:raise ValueError('Compatibility source differs from completed build')
        if patches:app['compatibilityPatches']=patches
    records=[copy_checked(source,target,expected)]
    with pefile.PE(str(source),fast_load=True) as pe:
        pe.parse_data_directories(directories=[1,13])
        imports=sorted({item.dll.decode('ascii') for field in ('DIRECTORY_ENTRY_IMPORT','DIRECTORY_ENTRY_DELAY_IMPORT') for item in getattr(pe,field,[])})
    # App-local Microsoft redistributables from the official fallback MSI;
    # other imports are Windows API contracts/system DLLs, recorded explicitly.
    for dependency in imports:
        if dependency.lower().startswith(('vcruntime','msvcp','concrt')):
            expected_dependency=fallback_hashes['program/'+dependency.lower()]
            records.append(copy_checked((payload if payload.exists() else runtime)/'program'/dependency,cores/app['id']/dependency,expected_dependency))
    app.update(nativeRuntime='Office/cores/'+app['id']+'/'+name,nativeSha256=records[0]['sha256'],
        nativeBytes=records[0]['bytes'],nativeFiles=records,nativeImports=imports,
        abi=1,buildToolchain='Rust 1.95.0 x86_64-pc-windows-msvc; one compiler job',
        baselineCli='Historical comparison only; no CLI executable ships or runs')
manifest['fallback']='libreoffice-runtime-manifest.json'
manifest['architecture']='Pinned source-core DLLs in a Pathweft-owned worker; bundled LibreOffice read-only fallback'
manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
source_root=payload if payload.exists() else runtime
if not args.cores_only:
    for file in fallback['files']:
        if digest(source_root/file['path'])!=file['sha256']:raise ValueError('Fallback input changed: '+file['path'])
if payload.exists() and not args.cores_only:
    if runtime.exists():raise FileExistsError('Preserve existing adopted fallback')
    # Same-volume adoption of a verified task-created payload; archives stay in
    # their separate cache directory and cannot enter the runtime item glob.
    payload.rename(runtime)
print(json.dumps(dict(cores=[dict(id=a['id'],files=a['nativeFiles'],imports=a['nativeImports']) for a in manifest['apps'] if 'nativeFiles' in a],fallbackVerification='unchanged-not-repeated' if args.cores_only else 'verified',fallbackFiles=len(fallback['files']),fallbackBytes=sum(f['bytes'] for f in fallback['files']))))
