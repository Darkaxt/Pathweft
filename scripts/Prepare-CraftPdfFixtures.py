"""Create source-owned PDF fixtures and an independent text oracle; no OCR."""
import hashlib
import io
import json
from pathlib import Path
import shutil
from PIL import Image,ImageDraw
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.utils import ImageReader
from pypdf import PdfReader,PdfWriter

ROOT=Path(__file__).resolve().parents[1]
source=Path('D:/Temp/pathweft-s16/craft-cores/sources/pdfcraft')
fonts=Path('D:/Temp/pathweft-s16/craft-cores/fonts/pdfcraft/fonts')
target=ROOT/'.local/evidence/2026-11-06-s16/pdf-inputs'
target.mkdir(parents=True,exist_ok=True)
pdfmetrics.registerFont(TTFont('FixtureInter',str(source/'assets/fonts/Inter-Regular.ttf')))
pdfmetrics.registerFont(TTFont('FixtureCjk',str(fonts/'biz-ud-pgothic/BIZUDPGothic-Regular.ttf')))
expected=[]
data=io.BytesIO();pdf=canvas.Canvas(data,pagesize=(600,1800),invariant=1)
pdf.setFillColorRGB(.07,.20,.67);pdf.rect(15,15,570,1770,fill=1,stroke=0)
pdf.setFillColorRGB(1,1,1);pdf.setFont('FixtureInter',12)
for index in range(65):
    text=f'Line {index:02d} - Located é "quoted" with stable source positions'
    pdf.drawString(35,1750-index*25,text);expected.append(text)
pdf.setFont('FixtureCjk',18);pdf.drawString(35,80,'子');expected.append('子')
pdf.showPage();pdf.setPageSize((600,400));pdf.setFillColorRGB(.86,.32,.12);pdf.rect(15,15,570,370,fill=1,stroke=0)
pdf.setFillColorRGB(1,1,1);pdf.setFont('FixtureInter',24);pdf.drawString(35,340,'Second page é');expected.append('Second page é')
pdf.save();(target/'located.pdf').write_bytes(data.getvalue())
oracle=[page.extract_text().strip() for page in PdfReader(io.BytesIO(data.getvalue())).pages]
if ''.join(oracle).replace('\n','')!=''.join(expected):raise ValueError('Independent fixture extraction differs')
(target/'expected.json').write_text(json.dumps(dict(lines=expected,pages=oracle),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for name,image in [('blank.pdf',False),('scanned.pdf',True)]:
    data=io.BytesIO();pdf=canvas.Canvas(data,pagesize=(600,400),invariant=1)
    if image:
        bitmap=Image.new('RGB',(300,180),'#1234ab');draw=ImageDraw.Draw(bitmap);draw.rectangle((80,40,220,140),fill='#dc5220')
        pdf.drawImage(ImageReader(bitmap),20,20,width=560,height=336)
    pdf.showPage();pdf.save();(target/name).write_bytes(data.getvalue())
writer=PdfWriter();writer.append(str(target/'located.pdf'));writer.encrypt('fixture-password',algorithm='AES-128')
with (target/'protected.pdf').open('wb') as output:writer.write(output)
(target/'invalid.pdf').write_bytes(b'%PDF-1.7\nnot a page tree')
shutil.copyfile(source/'crates/ui-egui/tests/data/form.pdf',target/'donor-form.pdf')
files=[dict(path=p.name,sha256=hashlib.sha256(p.read_bytes()).hexdigest(),bytes=p.stat().st_size) for p in sorted(target.iterdir()) if p.is_file() and p.name!='manifest.json']
(target/'manifest.json').write_text(json.dumps(dict(origin='Generated public-spec fixtures using pinned open-font inputs; donor form from PdfCraft pinned source; pypdf independent extraction oracle',files=files),indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(directory=str(target),files=len(files))))
