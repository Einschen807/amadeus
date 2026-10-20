"""ASR pipe integration check using only the model's bundled public sample."""
import base64
import json
import subprocess
import sys
import wave
from pathlib import Path

root = Path(sys.argv[1])
worker = Path(__file__).resolve().parents[1] / 'UnityCompanion/Assets/StreamingAssets/Voice/asr_worker.py'
sample = root / 'tools/asr/models/speech_paraformer-large_asr_nat-zh-cn-16k-common-vocab8404-pytorch/example/asr_example.wav'
with wave.open(str(sample), 'rb') as source:
    assert source.getframerate() == 16000 and source.getnchannels() == 1 and source.getsampwidth() == 2
    pcm = base64.b64encode(source.readframes(source.getnframes())).decode('ascii')
process = subprocess.Popen([str(root/'runtime/python.exe'), '-u', '-s', str(worker), str(root)],
                           stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                           text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
try:
    # communicate provides a bounded wait even if model loading or request handling hangs.
    requests = [{'id': 7, 'pcm': pcm}, {'id': 8, 'pcm': 'invalid!'}, {'id': 9, 'pcm': pcm}, {'command': 'quit'}]
    output, _ = process.communicate(''.join(json.dumps(x)+'\n' for x in requests), timeout=120)
    packets = [json.loads(line) for line in output.splitlines()]
    assert packets[0]['event'] == 'ready'
    assert packets[1]['id'] == 7 and '语音识别' in packets[1]['text']
    assert packets[2]['id'] == 8 and packets[2]['event'] == 'error'
    assert packets[3]['id'] == 9 and packets[3]['text'] == packets[1]['text']
    assert process.returncode == 0
    print('ASR_PIPE_OK: UTF-8 Chinese, memory PCM, invalid input recovery, graceful exit')
finally:
    if process.poll() is None:
        process.kill()
        process.wait()
