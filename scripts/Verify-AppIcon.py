"""Check actual ICO frames and render a contact sheet at native and enlarged sizes."""
import argparse
import io
import json
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw

parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--baseline-revision',help='Compare the actual committed previous icon without creating another copy')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
icon = Image.open(root / 'app.ico')
baseline=Image.open(io.BytesIO(subprocess.check_output(['git','-C',str(root),'show',args.baseline_revision+':app.ico']))) if args.baseline_revision else None
metrics=[]
sizes = [16, 24, 32, 48, 64, 128, 256]
sheet = Image.new('RGB', (920, 2 * 375), 'white')
draw = ImageDraw.Draw(sheet)
for row, background in enumerate(['#161616', '#f7f7f7']):
    top = row * 375
    draw.rectangle((0, top, 920, top + 375), fill=background)
    left = 16
    for size in sizes:
        assert (size, size) in icon.ico.sizes(), f'Missing {size}px ICO frame'
        frame = icon.ico.getimage((size, size)).convert('RGBA')
        pixels = list(frame.get_flattened_data())
        assert all(frame.getpixel(point)[3] == 0 for point in [(0, 0), (size-1, 0), (0, size-1), (size-1, size-1)])
        yellow = sum(a > 200 and r > 180 and g > 110 and b < 130 for r, g, b, a in pixels)
        bronze = sum(a > 200 and 40 < r < 170 and g < 110 and b < 65 for r, g, b, a in pixels)
        # At 16px antialiased point edges do not count as solid bronze pixels;
        # require a visible dark mark without treating its edge coverage as loss.
        assert yellow > size*size/4 and bronze > size*size/(50 if size < 24 else 30), f'Folder/compass contrast lost at {size}px'
        if row==0 and baseline:
            old=baseline.ico.getimage((size,size)).convert('RGBA')
            alpha=sum(pixel[3] for pixel in pixels)
            previous=sum(old.getchannel('A').get_flattened_data())
            metrics.append(dict(size=size,alphaAreaRatio=alpha/previous,bounds=frame.getbbox(),previousBounds=old.getbbox()))
        draw.text((left, top + 10), f'{size}px', fill='#a0a0a0')
        sheet.paste(frame, (left, top + 40), frame)
        if size <= 64:
            enlarged = frame.resize((size*3, size*3), Image.Resampling.NEAREST)
            sheet.paste(enlarged, (left, top + 120), enlarged)
        left += max(size + 22, 100)
args.output.parent.mkdir(parents=True, exist_ok=True)
sheet.save(args.output)
if metrics:
    typical=[m['alphaAreaRatio'] for m in metrics if m['size'] in (24,32,48)]
    assert 1.10<=sum(typical)/len(typical)<=1.16,'Taskbar artwork footprint increase differs from the requested range'
    args.output.with_suffix('.json').write_text(json.dumps(dict(baseline=args.baseline_revision,metric='Alpha-weighted visible artwork area, unchanged square canvas',frames=metrics),indent=2)+'\n')
    print(json.dumps(metrics))
print('All required transparent ICO frames retain yellow-folder and dark-compass contrast.')
