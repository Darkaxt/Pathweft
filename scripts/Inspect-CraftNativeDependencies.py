"""Read-only PE imports and produced PDF content; never executes a Craft binary."""
import hashlib
import json
from pathlib import Path
import pefile
import pypdfium2

ROOT=Path(__file__).resolve().parents[1]
EVIDENCE=ROOT/'.local/evidence/2026-11-04-specialists'
manifest=json.loads((ROOT/'vendor/Office/runtime-manifest.json').read_text())
results=[]
for app in manifest['apps']:
    executable=Path('D:/BuildCache/Pathweft/office')/app['id']/(app['id']+'-cli.exe')
    with pefile.PE(str(executable),fast_load=True) as pe:
        pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT'],pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT']])
        imports=sorted(set(item.dll.decode('ascii') for item in getattr(pe,'DIRECTORY_ENTRY_IMPORT',[])))
        delayed=sorted(set(item.dll.decode('ascii') for item in getattr(pe,'DIRECTORY_ENTRY_DELAY_IMPORT',[])))
        results.append(dict(app=app['id'],machine=pe.FILE_HEADER.Machine,subsystem=pe.OPTIONAL_HEADER.Subsystem,
            minimum_os=[pe.OPTIONAL_HEADER.MajorOperatingSystemVersion,pe.OPTIONAL_HEADER.MinorOperatingSystemVersion],imports=imports,delayed=delayed))
pdfs=[]
for app in manifest['apps']:
    folder=EVIDENCE/'craft-baseline'/app['id']
    for path in folder.glob('*.pdf'):
        pages=[]
        with pypdfium2.PdfDocument(str(path)) as document:
            for index in range(len(document)):
                page=document[index]
                text=page.get_textpage()
                try:content=text.get_text_range()
                finally:text.close()
                pages.append(dict(index=index+1,text=content))
                page.close()
        pdfs.append(dict(file=path.name,app=app['id'],bytes=path.stat().st_size,sha256=hashlib.sha256(path.read_bytes()).hexdigest(),pages=pages))
report=dict(native_imports=results,pdfs=pdfs)
(EVIDENCE/'craft-native-and-pdf.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
print(json.dumps(dict(imports=results,pdfs=[dict(file=p['file'],pages=len(p['pages']),characters=sum(len(x['text']) for x in p['pages'])) for p in pdfs]),ensure_ascii=False))
