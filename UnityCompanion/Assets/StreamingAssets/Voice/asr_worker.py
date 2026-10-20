"""Private stdin/stdout worker: 16 kHz PCM in memory, offline Chinese ASR on CPU."""
import base64
import json
import os
import sys
import time
from pathlib import Path

protocol = sys.stdout
protocol.reconfigure(encoding='utf-8')
sys.stdin.reconfigure(encoding='utf-8')
sys.stdout = sys.stderr  # Libraries may print; keep protocol output machine-readable.
os.environ['CUDA_VISIBLE_DEVICES'] = ''
os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['MODELSCOPE_OFFLINE'] = '1'


def send(value):
    protocol.write(json.dumps(value, ensure_ascii=False) + '\n')
    protocol.flush()


def main():
    root = Path(sys.argv[1]).resolve()
    model_path = root / 'tools/asr/models/speech_paraformer-large_asr_nat-zh-cn-16k-common-vocab8404-pytorch'
    if not (model_path / 'model.pt').is_file():
        raise RuntimeError('Missing local Chinese ASR model')
    import numpy as np
    from funasr import AutoModel
    model = AutoModel(model=str(model_path), device='cpu', ngpu=0, ncpu=4,
                      disable_update=True, disable_pbar=True, log_level='ERROR')
    send({'event': 'ready'})
    if '--self-test' in sys.argv:
        import soundfile as sf
        audio, rate = sf.read(str(model_path / 'example/asr_example.wav'), dtype='float32')
        began = time.perf_counter()
        result = model.generate(input=audio, fs=rate, disable_pbar=True)
        text = result[0].get('text', '') if result else ''
        send({'event': 'self_test', 'text': text, 'seconds': round(time.perf_counter()-began, 3)})
        return 0 if text.strip() else 1
    for line in sys.stdin:
        request_id = None
        try:
            request = json.loads(line)
            request_id = request.get('id')
            if request.get('command') == 'quit':
                break
            raw = base64.b64decode(request['pcm'], validate=True)
            if not raw or len(raw) % 2 or len(raw) > 16000 * 2 * 21:
                raise ValueError('Invalid audio length')
            audio = np.frombuffer(raw, dtype='<i2').astype(np.float32) / 32768.0
            began = time.perf_counter()
            result = model.generate(input=audio, fs=16000, disable_pbar=True)
            text = result[0].get('text', '') if result else ''
            send({'event': 'result', 'id': request_id, 'text': text.strip(),
                  'seconds': round(time.perf_counter()-began, 3)})
        except Exception as exc:
            send({'event': 'error', 'id': request_id, 'message': type(exc).__name__})
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as exc:
        send({'event': 'fatal', 'message': str(exc)})
        sys.exit(1)
