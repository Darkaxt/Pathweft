"""Import the exact reviewed npm UMD and retain its complete matching source."""
import argparse,hashlib,json,shutil,tarfile
from pathlib import Path

PACKAGE='9e3e0f5beb9c15a671025eb588c64dc8078a529121d1de809f8752bc422ad09d'
SOURCE='388b79545767527e6facb72de4714f91a97d534416867653467b651636234d21'
COMMIT='5beecae42b0beb07eaccc3a732560f56867a8301'
def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--downloads',type=Path,required=True)
    args=parser.parse_args()
    repo=Path(__file__).resolve().parent.parent
    cache=Path('D:/BuildCache/Pathweft/marked/18.1.2')
    package=args.downloads/'marked-18.1.2.tgz'
    source=args.downloads/'marked-18.1.2-source.tar.gz'
    assert hashlib.sha256(package.read_bytes()).hexdigest()==PACKAGE
    assert hashlib.sha256(source.read_bytes()).hexdigest()==SOURCE
    cache.mkdir(parents=True,exist_ok=True)
    vendor=repo/'vendor/Marked';vendor.mkdir(exist_ok=True)
    with tarfile.open(package) as archive:
        bundle=archive.extractfile('package/lib/marked.umd.js').read()
        license=archive.extractfile('package/LICENSE').read()
    assert b'marked v18.1.2' in bundle[:300]
    (repo/'Editor/marked.umd.js').write_bytes(bundle)
    (vendor/'LICENSE.md').write_bytes(license)
    for item in [package,source]: shutil.copyfile(item,cache/item.name)
    manifest={'version':'18.1.2','repository':'https://github.com/markedjs/marked','commit':COMMIT,
      'fixCommit':'0d2022025d3cd44750a3ffbb77e2da60635589ee','npmUrl':'https://registry.npmjs.org/marked/-/marked-18.1.2.tgz',
      'npmSha256':PACKAGE,'sourceArchive':source.name,'sourceSha256':SOURCE,
      'bundle':'Editor/marked.umd.js','bundleSha256':hashlib.sha256(bundle).hexdigest(),
      'licenseSha256':hashlib.sha256(license).hexdigest()}
    (vendor/'runtime-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(manifest))
if __name__=='__main__': main()
