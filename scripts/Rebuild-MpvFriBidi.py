"""Narrow native FriBidi refresh inside the existing supervised pinned chroot.

Invoke through wsl-build-gate and the donor's verified with-isolated-toolchain.
All generated files stay under /scratch/pathweft117; retained inputs are read-only.
The caller supplies source-bound libraries and validates the returned link map.
"""
import argparse, hashlib, json, os, subprocess, tarfile
from pathlib import Path

ROOT=Path('/scratch/pathweft117')
BASE=Path('/work/mpv-runtime')
SDK=Path('/work/build/contrib/x86_64-w64-mingw32')
PREFIX=ROOT/'prefix'

def digest(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()
def run(*argv):
    print('+',*map(str,argv),flush=True)
    subprocess.run(list(map(str,argv)),check=True)
def extract(archive,target):
    target.mkdir(parents=True,exist_ok=False)
    with tarfile.open(archive) as source: source.extractall(target,filter='data')

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--resume-configured',action='store_true');args=parser.parse_args()
    if not str(Path('/proc/self/cgroup').read_text()).find('/llm-coordinator/')>=0:
        raise RuntimeError('The owned Coordinator cgroup is required')
    inputs=json.loads((ROOT/'inputs.json').read_text())
    for item in inputs['archives']:
        assert digest(ROOT/item['name'])==item['sha256'],item['name']
    before={str(p):digest(p) for p in sorted((BASE/'prefix/lib').glob('*.a'))}
    before.update({str(p):digest(p) for p in sorted((SDK/'lib').glob('*.a'))})
    os.environ.update(JOBS='1',CMAKE_BUILD_PARALLEL_LEVEL='1',CARGO_BUILD_JOBS='1',
        PKG_CONFIG_LIBDIR=f'{PREFIX}/lib/pkgconfig:{BASE}/prefix/lib/pkgconfig:{SDK}/lib/pkgconfig')
    cross=(ROOT/'cross.meson')
    text=(BASE/'cross.meson').read_text()
    text=text.replace("'-B/work/runtime-crt-only/'",f"'-B/work/runtime-crt-only/', '-B{PREFIX}/lib/'")
    text=text.replace("c_args = [",f"c_args = ['-I{PREFIX}/include', ")
    text=text.replace("cpp_args = [",f"cpp_args = ['-I{PREFIX}/include', ")
    text=text.replace("c_link_args = [",f"c_link_args = ['-L{PREFIX}/lib', ")
    text=text.replace("cpp_link_args = [",f"cpp_link_args = ['-L{PREFIX}/lib', ")
    cross.write_text(text)
    if not args.resume_configured:
        extract(ROOT/'fribidi-1.0.18.tar.xz',ROOT/'fribidi-source')
        fribidi=ROOT/'fribidi-source/fribidi-1.0.18'
        run('meson','setup',ROOT/'fribidi-build',fribidi,'--cross-file',cross,'--prefix',PREFIX,
            '--libdir','lib','--wrap-mode=nofallback','-Ddefault_library=static','-Dbuildtype=release',
            '-Ddocs=false','-Dbin=false','-Dtests=false')
        run('ninja','-C',ROOT/'fribidi-build','-j1')
        run('meson','install','-C',ROOT/'fribidi-build','--no-rebuild')
        extract(ROOT/inputs['mpvArchive'],ROOT/'mpv-source')
        mpv=next((ROOT/'mpv-source').iterdir())
        with (ROOT/'mpv-wasapi-session.patch').open('rb') as patch:
            subprocess.run(['patch','-p1','--batch'],cwd=mpv,stdin=patch,check=True)
    else:
        assert (ROOT/'mpv-build/build.ninja').is_file()
        assert (PREFIX/'lib/libfribidi.a').is_file()
        mpv=next((ROOT/'mpv-source').iterdir())
    # libass's SDK -L flag otherwise precedes its transitive FriBidi -L flag.
    # Give Meson the exact new static archive, not an ambiguous -lfribidi.
    pc=PREFIX/'lib/pkgconfig/fribidi.pc';description=pc.read_text()
    assert 'Version: 1.0.18' in description
    assert 'Libs: -L${libdir} -lfribidi' in description
    pc.write_text(description.replace('Libs: -L${libdir} -lfribidi',f'Libs: {PREFIX}/lib/libfribidi.a'))
    configure=['meson','setup']
    if args.resume_configured:configure+=['--reconfigure','--clearcache']
    run(*configure,ROOT/'mpv-build',mpv,'--cross-file',cross,'--prefix',PREFIX,'--libdir','lib',
        '--wrap-mode=nofallback','-Ddefault_library=shared','-Dprefer_static=true',
        '-Db_lto=false','-Doptimization=2','-Ddebug=false','-Db_ndebug=true',
        '-Dauto_features=disabled','-Dlibmpv=true','-Dcplayer=false','-Dbuild-date=false',
        '-Dgpl=true','-Dlua=disabled','-Dd3d11=enabled','-Dd3d-hwaccel=enabled',
        '-Dwasapi=enabled','-Dwin32-threads=enabled','-Diconv=enabled','-Dshaderc=enabled',
        '-Dspirv-cross=enabled','-Duchardet=enabled','-Dzlib=enabled','-Dgl=disabled','-Dvulkan=disabled')
    # Meson resolves static pkg-config dependencies to absolute library paths.
    # Require the new archive before compiling; an -L flag alone is insufficient.
    ninja=(ROOT/'mpv-build/build.ninja').read_text()
    link=next(line for line in ninja.splitlines() if line.startswith(' LINK_ARGS =') and 'libfribidi.a' in line)
    assert str(PREFIX/'lib/libfribidi.a') in link
    assert str(SDK/'lib/libfribidi.a') not in link
    # Add a link map using supported linker flags, solely in the isolated build.
    run('meson','configure',ROOT/'mpv-build',
        "-Dc_link_args="+repr([f'-L{PREFIX}/lib',f'-L{BASE}/prefix/lib',f'-L{SDK}/lib',
        '-L/work/runtime-crt-only','-static','-static-libgcc','-static-libstdc++','-pthread',f'-Wl,-Map,{ROOT}/mpv-link.map']),
        "-Dcpp_link_args="+repr([f'-L{PREFIX}/lib',f'-L{BASE}/prefix/lib',f'-L{SDK}/lib',
        '-L/work/runtime-crt-only','-static','-static-libgcc','-static-libstdc++','-pthread',f'-Wl,-Map,{ROOT}/mpv-link.map']))
    run('ninja','-C',ROOT/'mpv-build','-j1')
    run('meson','install','-C',ROOT/'mpv-build','--no-rebuild')
    unstripped=digest(PREFIX/'bin/libmpv-2.dll')
    run('x86_64-w64-mingw32-strip','--strip-all',PREFIX/'bin/libmpv-2.dll')
    maptext=(ROOT/'mpv-link.map').read_text()
    assert str(PREFIX/'lib/libfribidi.a') in maptext
    assert str(SDK/'lib/libfribidi.a') not in maptext
    for name,expected in before.items(): assert digest(Path(name))==expected,name
    receipt={'schema':1,'fribidi':'1.0.18','fribidiArchive':digest(PREFIX/'lib/libfribidi.a'),
        'runtime':digest(PREFIX/'bin/libmpv-2.dll'),'unstrippedRuntime':unstripped,'linkMap':digest(ROOT/'mpv-link.map'),
        'borrowedInputsUnchanged':before,'inputs':inputs,'crossFile':text,
        'buildRecipe':digest(Path(__file__))}
    (ROOT/'result.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps({'runtime':receipt['runtime'],'fribidi':receipt['fribidi'],'borrowedInputsUnchanged':len(before)}),flush=True)

if __name__=='__main__': main()
