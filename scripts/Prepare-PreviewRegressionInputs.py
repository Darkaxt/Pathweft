"""Small retained SDR/audio fixtures for current and delivered preview qualification."""
import hashlib
import json
from pathlib import Path
import runpy
import wave

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / '.local/evidence/2026-11-04-specialists/inputs'
SCRATCH = Path('D:/Temp/pathweft-s13-s9-s11/media-fixtures')
SCRATCH.mkdir(parents=True, exist_ok=True)
OUTPUT.mkdir(parents=True, exist_ok=True)
raw = SCRATCH / 'synthetic.y4m'
with raw.open('wb') as video:
    video.write(b'YUV4MPEG2 W160 H90 F12:1 Ip A1:1 C420jpeg\n')
    for frame in range(36):
        video.write(b'FRAME\n' + bytes([50 + frame * 3]) * (160 * 90)
                    + bytes([150]) * (80 * 45) + bytes([90]) * (80 * 45))
wrapper = Path.home() / '.codex/skills/yt-dlp-ffmpeg/scripts/invoke_media_tool.py'
runpy.run_path(str(wrapper))  # Installed helper's runtime resolution; no tool installation.
from agent_toolbelt_media import media
result = media.invoke_transcode(input_path=str(raw), output_path=str(OUTPUT / 'sdr.mp4'),
    ffmpeg_args=['-c:v','libx264','-threads','1','-pix_fmt','yuv420p','-an'],
    timeout_sec=None)  # Observe actual finite encoder completion, without a kill deadline.
if not result['ok']:
    raise RuntimeError(json.dumps(result))
with wave.open(str(OUTPUT / 'audio-playback.wav'),'wb') as audio:
    audio.setnchannels(1)
    audio.setsampwidth(2)
    audio.setframerate(8000)
    audio.writeframes(b'\0\0' * 24_000)
inputs = [dict(name='sdr',path=str(OUTPUT / 'sdr.mp4')),
          dict(name='audio',path=str(OUTPUT / 'audio-playback.wav'))]
inputs += json.loads((OUTPUT.parent / 'media-inputs.json').read_text())
for item in inputs:
    data = Path(item['path']).read_bytes()
    item.update(bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
manifest = OUTPUT.parent / 'playback-inputs.json'
manifest.write_text(json.dumps(inputs,indent=2),encoding='utf-8')
print(json.dumps(dict(ok=True,inputs=str(manifest),native=result)))
