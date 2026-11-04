"""Retrieve pinned, inert upstream fixtures. Never execute inspected binaries.

Inputs are retained private validation assets on D:, not product redistributables.
The same files support repeatable S13 and packaged-release qualification.
"""
import hashlib
import json
import pathlib
import urllib.request
import urllib.parse
import plistlib
import zipfile
import io
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1] / '.local/evidence/2026-11-04-specialists/inputs'
FILES = [
    ('hello.elf', 'konrad-kruczynski/elfsharp-test-binaries', '0a002dfc12e706695d1dc1cc11ec7728b109afe1', 'hello64le'),
    ('hello.dylib', 'konrad-kruczynski/elfsharp-test-binaries', '0a002dfc12e706695d1dc1cc11ec7728b109afe1', 'simple-mach-o'),
    ('kernel.uimage', 'konrad-kruczynski/elfsharp-test-binaries', '0a002dfc12e706695d1dc1cc11ec7728b109afe1', 'uImage-panda'),
    ('windows10.pf', 'EricZimmerman/Prefetch', '54a3ac0d6d0006d3836c9bd368dfe32cbcaa131d', 'Prefetch.Test/TestFiles/Win10/CMD.EXE-D269B812.pf'),
    ('windows7.pf', 'EricZimmerman/Prefetch', '54a3ac0d6d0006d3836c9bd368dfe32cbcaa131d', 'Prefetch.Test/TestFiles/Win7/CMD.EXE-4A81B364.pf'),
    ('sample.apk', 'google/bundletool', '586a43a450712a1067f3d92cf7574dee68226302', 'src/test/resources/com/android/tools/build/bundletool/testdata/apk/com.test.app.apk'),
    ('sample.aab', 'google/bundletool', '586a43a450712a1067f3d92cf7574dee68226302', 'src/test/resources/com/android/tools/build/bundletool/testdata/bundle/install-time-permanent-modules.aab'),
    ('document.docx', 'dotnet/Open-XML-SDK', '431ab05cf160248cc3885a4a766026d4f8243792', 'test/DocumentFormat.OpenXml.Tests.Assets/assets/TestFiles/HelloWorld.docx'),
    ('spreadsheet.xlsx', 'dotnet/Open-XML-SDK', '431ab05cf160248cc3885a4a766026d4f8243792', 'test/DocumentFormat.OpenXml.Tests.Assets/assets/TestFiles/basicspreadsheet.xlsx'),
    ('presentation.pptx', 'dotnet/Open-XML-SDK', '431ab05cf160248cc3885a4a766026d4f8243792', 'test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/presentation/pptxtestfile.pptx'),
    ('slides.pptx', 'dotnet/Open-XML-SDK', '431ab05cf160248cc3885a4a766026d4f8243792', 'test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/presentation/MetroCodeFree.pptx'),
    ('navigation.pptx', 'dotnet/Open-XML-SDK', '431ab05cf160248cc3885a4a766026d4f8243792', 'test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/presentation/AllSlidesConverterSC.pptx'),
    ('crash.dmp', 'google/breakpad', '94c818ea27482d8a8d32b0c8de50cbca0270db9a', 'src/processor/testdata/ascii_read_av.dmp'),
]
RELEASES = [
    ('7zip-installer.exe', 'https://github.com/ip7z/7zip/releases/download/26.05/7z2605-x64.exe', 'fd99e86f57d8f429ef5ed045bf98315af962e2309b0f1ba87a147a44c71647d0'),
    ('7zip.msi', 'https://github.com/ip7z/7zip/releases/download/26.05/7z2605-x64.msi', '3fe4ab99fa15629c652944309a2b2bc6e41fc933fc6b63f5df1ebcf3aea68263'),
]

def retrieve(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'Pathweft-fixture-qualification'})) as response:
        data = response.read(32 * 1024 * 1024 + 1)
    if len(data) > 32 * 1024 * 1024:
        raise ValueError('Fixture exceeds bounded download size')
    return data

def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    previous={item['name']:item for item in json.loads((ROOT/'manifest.json').read_text())} if (ROOT/'manifest.json').exists() else {}
    # Object identity from the already inspected official pinned Git tree.
    known_blobs={'navigation.pptx':'424d89022a5b914def86a988010af42fc8c0b04c'}
    manifest = []
    for name, repo, revision, source in FILES:
        source_path = urllib.parse.quote(source, safe='/')
        url = f'https://raw.githubusercontent.com/{repo}/{revision}/{source_path}'
        target = ROOT / name
        if not target.exists():
            target.write_bytes(retrieve(url))
        data = target.read_bytes()
        expected=known_blobs.get(name) or (previous.get(name,{}).get('git_blob') if previous.get(name,{}).get('url')==url else None)
        if expected is None: expected=json.loads(retrieve(f'https://api.github.com/repos/{repo}/contents/{source_path}?ref={revision}'))['sha']
        actual = hashlib.sha1(f'blob {len(data)}\0'.encode() + data).hexdigest()
        if actual != expected:
            raise ValueError(f'Pinned Git object mismatch: {name}')
        manifest.append(dict(name=name, url=url, git_blob=actual, sha256=hashlib.sha256(data).hexdigest(), size=len(data)))
    for name, url, expected in RELEASES:
        target = ROOT / name
        if not target.exists():
            target.write_bytes(retrieve(url))
        data = target.read_bytes()
        actual = hashlib.sha256(data).hexdigest()
        if actual != expected:
            raise ValueError(f'Official release digest mismatch: {name}')
        manifest.append(dict(name=name, url=url, sha256=actual, size=len(data)))
    # Inert generated variants exercise real navigation and binary-plist paths.
    # Preserve pinned originals; record each derived input's identity separately.
    with zipfile.ZipFile(ROOT/'document.docx') as source: word=source.read('word/document.xml')
    # Preserve namespace declarations referenced by mc:Ignorable attribute
    # values; ElementTree serialization drops those unused namespace prefixes.
    paragraphs=''.join('<w:p><w:r><w:br w:type="page"/><w:t>Pathweft navigation page '+str(number)+'</w:t></w:r></w:p>' for number in range(2,65)).encode()
    if b'<w:sectPr' not in word: raise ValueError('Pinned Word fixture section changed')
    word=word.replace(b'<w:sectPr',paragraphs+b'<w:sectPr',1)
    output=io.BytesIO()
    with zipfile.ZipFile(ROOT/'document.docx') as source,zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as target:
        for member in source.infolist(): target.writestr(member,word if member.filename=='word/document.xml' else source.read(member))
    derived={'document-pages.docx':output.getvalue(),'identity.bplist':plistlib.dumps({'CFBundleIdentifier':'org.pathweft.binary','CFBundleVersion':'42','UIDeviceFamily':[1,2]},fmt=plistlib.FMT_BINARY)}
    for name,data in derived.items():
        (ROOT/name).write_bytes(data)
        manifest.append(dict(name=name,generated='Inert bounded fixture from pinned Word input or Python standard plistlib; never product source',sha256=hashlib.sha256(data).hexdigest(),size=len(data)))
    (ROOT / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(json.dumps(dict(root=str(ROOT), fixtures=len(manifest))))

def navigation_fixture():
    manifest=json.loads((ROOT/'manifest.json').read_text())
    original=next(item for item in manifest if item['name']=='navigation.pptx')
    source=(ROOT/'navigation.pptx').read_bytes()
    if hashlib.sha256(source).hexdigest()!=original['sha256']:raise ValueError('Pinned presentation fixture changed')
    target=io.BytesIO();labels=[]
    with zipfile.ZipFile(io.BytesIO(source)) as archive,zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as output:
        for member in archive.infolist():
            content=archive.read(member)
            match=re.fullmatch(r'ppt/slides/slide(\d+)\.xml',member.filename)
            if match:
                label='Pathweft slide '+match[1]
                content,count=re.subn(rb'<a:t>.*?</a:t>',b'<a:t>'+label.encode()+b'</a:t>',content,count=1,flags=re.DOTALL)
                if count!=1:raise ValueError('Pinned slide text structure changed')
                labels.append(label)
            output.writestr(member,content)
    data=target.getvalue();name='navigation-distinct.pptx'
    (ROOT/name).write_bytes(data)
    manifest=[item for item in manifest if item['name']!=name]
    manifest.append(dict(name=name,generated='Distinct inert slide labels derived from pinned navigation.pptx; original namespaces and other parts preserved',sha256=hashlib.sha256(data).hexdigest(),size=len(data)))
    (ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print(json.dumps(dict(derived=name,labels=labels)))

if __name__ == '__main__':
    if sys.argv[1:] not in ([],['--navigation-only']):raise SystemExit('Use --navigation-only or no arguments')
    if not sys.argv[1:]:main()
    navigation_fixture()
