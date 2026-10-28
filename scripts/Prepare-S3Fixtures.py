"""Finite native-preview fixtures; stdlib only, all output in registered scratch."""
import argparse
from pathlib import Path
import shutil
import struct
import wave
import zipfile
import zlib

parser = argparse.ArgumentParser()
parser.add_argument('--scratch-root', required=True)
scratch = Path(parser.parse_args().scratch_root).resolve()
if not str(scratch).lower().startswith('d:\\temp\\pathweft-s3-') or scratch.parent != Path('D:/Temp'):
    raise SystemExit('Use one registered S3 native scratch root.')
files = scratch / 'fixture' / 'files'
files.mkdir(parents=True, exist_ok=True)
(files / 'pwfts3_sample.txt').write_text('Pathweft internal preview\nUnicode: é ă 子\n', encoding='utf-8')
(files / 'pwfts3_sample.cs').write_text('public class PreviewSample { /* read only */ }\n', encoding='utf-8')
(files / 'pwfts3_utf16.txt').write_text('UTF-16: é 子\n', encoding='utf-16')
(files / 'pwfts3_sample.md').write_text('# Pathweft preview\n\nA **shared** viewer for *every surface*.\n\n- Text and code\n- Safe native formatting\n\n```cs\nConsole.WriteLine("not executed");\n```\n\n<script>window.__preview_executed = true</script>\n\n[Inactive link](javascript:alert(1))\n![Inactive remote image](https://example.invalid/probe.png)\n', encoding='utf-8')
(files / 'pwfts3_sample.html').write_text('<script>window.__preview_executed = true</script><h1>source only</h1>', encoding='utf-8')
(files / 'pwfts3_large.txt').write_text('bounded preview\n' * 80_000, encoding='utf-8')
(files / 'pwfts3_bad.pdf').write_bytes(b'%PDF-broken')
(files / 'pwfts3_bad.png').write_bytes(b'broken image')
(files / 'pwfts3_unsupported.bin').write_bytes(b'\x00\x00\x01\x02' * 50)
(files / 'pwfts3_sample.docm').write_bytes(b'\x00\x00 document macros must never run')
with (files / 'pwfts3_huge.png').open('wb') as stream:
    stream.truncate(65 * 1024 * 1024)

def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))

width, height = 320, 180
pixels = b''.join(b'\x00' + bytes((40 + y // 3, 90, 190)) * width for y in range(height))
png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(pixels)) + chunk(b'IEND', b'')
(files / 'pwfts3_sample.png').write_bytes(png)

objects = [
    b'<< /Type /Catalog /Pages 2 0 R >>',
    b'<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>',
    b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /Font << /F1 7 0 R >> >> /Contents 4 0 R >>',
    None,
    b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R >>',
    None,
    b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
]
for index, line in ((3, b'Pathweft PDF page one'), (5, b'Pathweft PDF page two')):
    content = b'BT /F1 28 Tf 55 700 Td (' + line + b') Tj ET\n'
    objects[index] = b'<< /Length ' + str(len(content)).encode() + b' >>\nstream\n' + content + b'endstream'
pdf = bytearray(b'%PDF-1.4\n')
offsets = [0]
for number, value in enumerate(objects, 1):
    offsets.append(len(pdf))
    pdf += f'{number} 0 obj\n'.encode() + value + b'\nendobj\n'
xref = len(pdf)
pdf += f'xref\n0 {len(offsets)}\n0000000000 65535 f \n'.encode()
for offset in offsets[1:]:
    pdf += f'{offset:010d} 00000 n \n'.encode()
pdf += f'trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n'.encode()
(files / 'pwfts3_sample.pdf').write_bytes(pdf)
with zipfile.ZipFile(files / 'pwfts3_sample.zip', 'w') as archive:
    archive.writestr('readme.txt', 'Listed, never extracted.')
    archive.writestr('../escaped.exe', b'\x00\x00not executed')
with wave.open(str(files / 'pwfts3_sample.wav'), 'wb') as audio:
    audio.setnchannels(1)
    audio.setsampwidth(2)
    audio.setframerate(8000)
    audio.writeframes(b'\x00\x00' * 16000)
font = Path('C:/Windows/Fonts/segoeui.ttf')
if not font.is_file():
    raise SystemExit('Expected read-only Windows font fixture source is unavailable.')
shutil.copyfile(font, files / 'pwfts3_sample.ttf')
with (scratch / 'fixture' / 'pwfts3_video.y4m').open('wb') as video:
    video.write(b'YUV4MPEG2 W160 H90 F12:1 Ip A1:1 C420jpeg\n')
    for frame in range(24):
        video.write(b'FRAME\n' + bytes([50 + frame * 4]) * (160 * 90) + bytes([150]) * (80 * 45) + bytes([90]) * (80 * 45))
print('Prepared text/Markdown, image, two-page PDF, ZIP, font, silent PCM audio, raw video and bounded-error fixtures.')
