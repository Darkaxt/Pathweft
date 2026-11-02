"""Import only the qualified isolated refresh; preserve the donor and .1 cache."""
import argparse,hashlib,json,shutil,tarfile
from pathlib import Path

OLD=Path('D:/BuildCache/Pathweft/mpv/e471496c56-pathweft.1')
NEW=OLD.with_name('e471496c56-pathweft.2')
FRIBIDI='1de5e8073d035388b71d0b2d452b1da1f8caeea8a7702997b401fe1b6250727f'
def digest(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def save(path,data):path.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8',newline='\n')
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--build',type=Path,required=True);args=parser.parse_args()
    root=args.build;repo=Path(__file__).resolve().parent.parent
    receipt=json.loads((root/'result.json').read_text())
    assert receipt['fribidi']=='1.0.18'
    assert receipt['inputs']['archives'][0]['sha256']==FRIBIDI
    assert digest(root/'prefix/bin/libmpv-2.dll')==receipt['runtime']
    assert digest(root/'mpv-link.map')==receipt['linkMap']
    assert digest(repo/'scripts/Rebuild-MpvFriBidi.py')==receipt['buildRecipe']
    # Each linked input must still match its actual read-only retained file.
    retained=Path('D:/VLC-Redesign/build/windows-x64/toolchain')
    for name,expected in receipt['borrowedInputsUnchanged'].items():
        assert digest(retained/name.lstrip('/'))==expected,name
    assert not NEW.exists(),'Preserve an existing cache; do not overwrite it'
    NEW.mkdir(parents=True)
    manifest=json.loads((OLD/'runtime-manifest.json').read_text())
    for name,expected in manifest['runtime'].items():assert digest(OLD/name)==expected
    shutil.copyfile(root/'prefix/bin/libmpv-2.dll',NEW/'libmpv-2.dll')
    shutil.copyfile(OLD/'libspirv-cross-c-shared.dll',NEW/'libspirv-cross-c-shared.dll')
    sources=NEW/'sources';sources.mkdir()
    for source in (OLD/'sources').iterdir():
        if source.name=='fribidi-1.0.16.tar.xz':continue
        if source.is_dir():shutil.copytree(source,sources/source.name)
        else:shutil.copyfile(source,sources/source.name)
    shutil.copyfile(root/'fribidi-1.0.18.tar.xz',sources/'fribidi-1.0.18.tar.xz')
    assert digest(sources/'fribidi-1.0.18.tar.xz')==FRIBIDI
    overlay=sources/'build-recipes/pathweft';overlay.mkdir()
    shutil.copyfile(repo/'scripts/Rebuild-MpvFriBidi.py',overlay/'Rebuild-MpvFriBidi.py')
    for name in ['inputs.json','result.json','mpv-link.map','cross.meson']:
        shutil.copyfile(root/name,overlay/name)
    notices=json.loads((OLD/'licenses/manifest.json').read_text())
    notices['archives']=[item for item in notices['archives'] if not item['archive'].startswith('fribidi-')]
    for item in notices['archives']:
        for notice in item['notices']:
            source=OLD/'licenses'/notice['file'];assert digest(source)==notice['sha256']
            target=NEW/'licenses'/notice['file'];target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target)
    fribidi={'archive':'fribidi-1.0.18.tar.xz','sha256':FRIBIDI,'bytes':(sources/'fribidi-1.0.18.tar.xz').stat().st_size,
        'url':'https://github.com/fribidi/fribidi/releases/download/v1.0.18/fribidi-1.0.18.tar.xz','notices':[]}
    with tarfile.open(sources/fribidi['archive']) as archive:
        for name in ['AUTHORS','COPYING']:
            path='fribidi-1.0.18/'+name;content=archive.extractfile(path).read();relative=FRIBIDI[:16]+'/'+path
            target=NEW/'licenses'/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(content)
            fribidi['notices'].append({'archive_path':path,'file':relative,'bytes':len(content),'sha256':hashlib.sha256(content).hexdigest()})
    notices['archives'].append(fribidi);save(NEW/'licenses/manifest.json',notices)
    manifest['schema']=2;manifest['cacheRevision']='e471496c56-pathweft.2'
    manifest['runtime']['libmpv-2.dll']=receipt['runtime']
    manifest['sources']=[item for item in manifest['sources'] if not item['name'].startswith('fribidi-')]
    manifest['sources'].append({'name':fribidi['archive'],'sha256':FRIBIDI})
    manifest['refresh']={'fribidi':'1.0.18','recipe':'scripts/Rebuild-MpvFriBidi.py','recipeSha256':receipt['buildRecipe'],
        'linkMapSha256':receipt['linkMap'],'baseBuildInfoSha256':digest(OLD/'MPV-BUILDINFO.json')}
    save(NEW/'runtime-manifest.json',manifest);save(repo/'vendor/MPV/runtime-manifest.json',manifest)
    original=json.loads((OLD/'MPV-BUILDINFO.json').read_text())
    build={'schema':'pathweft.mpv_build.v1','lineage':{key:manifest[key] for key in ['donor','donorTag','donorCommit','sourceArchiveSha256']},
      'baseBuildInfoSha256':digest(OLD/'MPV-BUILDINFO.json'),'files':[{'name':name,'sha256':digest(NEW/name),'bytes':(NEW/name).stat().st_size} for name in manifest['runtime']],
      'sources':manifest['sources'],'host_build_tools':original['host_build_tools'],'api_headers_sha256':original['api_headers_sha256'],
      'patch':original['patch'],'patch_sha256':original['patch_sha256'],'refresh':manifest['refresh'],
      'licensesManifestSha256':digest(NEW/'licenses/manifest.json'),'output':manifest['output'],
      'release_symbols':{'method':'GNU strip-all on the isolated output','unstrippedSha256':receipt['unstrippedRuntime']},
      'system_dependencies':original['system_dependencies']}
    save(NEW/'MPV-BUILDINFO.json',build)
    print(json.dumps({'cache':str(NEW),'runtime':manifest['runtime'],'fribidi':fribidi['archive']}))
if __name__=='__main__':main()
