"""Bounded disposable archive corpus; native containers are prepared by the companion PS script."""
import argparse
import binascii
import gzip
import io
from pathlib import Path
import random
import struct
import tarfile
import zipfile
import zlib

args = argparse.ArgumentParser()
args.add_argument('--scratch-root', required=True)
root = Path(args.parse_args().scratch_root)
if not str(root.resolve()).lower().startswith('d:\\temp\\pathweft-s4-') and str(root.resolve()).lower() != 'd:\\temp\\pathweft-rars-native':
    raise ValueError('Use registered S4 native scratch')
fixture = root / 'fixture'
files = fixture / 'source'
(files / 'nested').mkdir(parents=True, exist_ok=True)
(files / 'empty').mkdir(exist_ok=True)
(fixture / 'containers').mkdir(exist_ok=True)
(fixture / 'destination').mkdir(exist_ok=True)
(files / 'plain 子 spaced.txt').write_text('Pathweft native archive fixture · 子\n', encoding='utf-8')
(files / 'nested' / 'readme.md').write_text('# Inside the archive\n\n**Native preview** keeps this source closed.\n', encoding='utf-8')
(files / 'nested' / 'second.txt').write_text('Nested file retained by transactional edits.\n', encoding='utf-8')
(files / 'payload.bin').write_bytes(random.Random(41).randbytes(240_000))
def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', binascii.crc32(kind + data) & 0xffffffff)
png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>2I5B', 80, 60, 8, 2, 0, 0, 0))
png += chunk(b'IDAT', zlib.compress(b''.join(b'\0' + bytes((60, 130, 230)) * 80 for _ in range(60)))) + chunk(b'IEND', b'')
(files / 'image.png').write_bytes(png)
with tarfile.open(fixture / 'containers' / 'wrapped.tgz', 'w:gz') as archive:
    archive.add(files / 'nested', arcname='nested')
with gzip.open(fixture / 'containers' / 'single.txt.gz', 'wb') as output:
    output.write(b'GZIP read-only native member')
for name, entries in {
    'traversal.zip': {'../escape.txt': b'not extracted'},
    'duplicate.zip': {'same.txt': b'first', 'SAME.txt': b'second'},
}.items():
    with zipfile.ZipFile(fixture / 'containers' / name, 'w') as archive:
        for key, data in entries.items(): archive.writestr(key, data)
(fixture / 'containers' / 'broken.7z').write_bytes(b'not an archive')
# Tiny stored RAR5 fixture from libarchive v3.8.2; validation only, never executed.
# https://github.com/libarchive/libarchive/blob/v3.8.2/libarchive/test/test_read_format_rar5_stored.rar.uu
# Copyright libarchive contributors; BSD-2-Clause, see tests/ArchiveContracts/NOTICE.txt.
rar5 = [
    b"M4F%R(1H'`0`SDK7E\"@$%!@`%`0&`@``X,`9C+`(#\"YT`!)T`I(,\"M$.@E8``",
    b"M`0YH96QL;W=O<FQD+G1X=`H#$WX.JUM6Z0X::&5L;&\\@;&EB87)C:&EV92!T",
    b"397-T('-U:71E(0H==U91`P4$````",
]
(fixture / 'containers' / 'sample.rar').write_bytes(b''.join(binascii.a2b_uu(line) for line in rar5))
print(f'Prepared bounded corpus: {fixture}')
