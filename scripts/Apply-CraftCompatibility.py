"""Apply exact, source-bound compatibility edits before selecting a Craft core."""
import argparse
import hashlib
import json
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--engine',required=True)
parser.add_argument('--source',required=True,type=Path)
args=parser.parse_args()
records=[]
if args.engine=='figcraft':
    relative='crates/pathops/src/budget.rs'
    path=args.source/relative
    original='e7104635bd3c9c4cd898029973f13d58e8a862e579f8aae88d929094663efb4d'
    before=path.read_bytes()
    # Identical unsigned low-bit operation, supported on stable Rust 1.95.
    # Fenwick's two loops already guarantee i > 0. No budget is changed.
    old=b'i.isolate_lowest_one()'
    new=b'(i & i.wrapping_neg())'
    if hashlib.sha256(before).hexdigest()==original:
        if before.count(old)!=2:raise ValueError('Unexpected low-bit call count')
        after=before.replace(old,new)
        path.write_bytes(after)
    else:
        restored=before.replace(new,old)
        if before.count(new)!=2 or hashlib.sha256(restored).hexdigest()!=original:
            raise ValueError('FigCraft compatibility source differs from the pinned input')
        after=before
    records.append(dict(path=relative,originalSha256=original,sha256=hashlib.sha256(after).hexdigest(),
        reason='Stable Rust equivalent of unsigned isolate_lowest_one; unchanged sweep budgets'))
print(json.dumps(records))
