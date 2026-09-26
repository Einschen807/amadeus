"""Build DX9-compatible copies; original models and textures stay untouched."""
import json
import struct
from pathlib import Path
from PIL import Image
root = Path(__file__).resolve().parent.parent
out = root / 'EmoteTest' / 'cache'
out.mkdir(exist_ok=True)
names = ['牧瀬紅莉栖ab_白衣', '牧瀬紅莉栖ab', '牧瀬紅莉栖c_白衣', '牧瀬紅莉栖c']
source = root / 'extracted_emote' / 'decompiled_characters'
def array(blob, pos):
    size = blob[pos] - 12
    count = int.from_bytes(blob[pos+1:pos+1+size], 'little')
    pos += 1 + size
    width = blob[pos] - 12
    pos += 1
    return [int.from_bytes(blob[pos+k*width:pos+(k+1)*width], 'little') for k in range(count)]

for i, name in enumerate(names):
    base = source / (name + '.psb.m')
    data = json.loads(Path(str(base) + '.json').read_text('utf-8-sig'))
    resources = json.loads(Path(str(base) + '.resx.json').read_text('utf-8-sig'))
    for section in ('Resources', 'ExtraResources'):
        for key, value in resources.get(section, {}).items():
            resources[section][key] = str(source / value)
    blob = (root / 'extracted_emote' / 'motion' / 'motion' / (name + '.psb.m')).read_bytes()
    off, lens, start = struct.unpack_from('<3I', blob, 24)
    offsets, lengths = array(blob, off), array(blob, lens)
    for tex in data['source'].values():
        if 'texture' not in tex:
            continue
        t = tex['texture']
        index = int(t['pixel'].split('#')[-1])
        raw = blob[start+offsets[index]:start+offsets[index]+lengths[index]]
        assert t['type'] == 'BC7'
        assert len(raw) == ((t['width']+3)//4)*((t['height']+3)//4)*16
        image = Image.frombytes('RGBA', (t['width'], t['height']), raw, 'bcn', (7, 'BC7'))
        png = out / ('kurisu%d-tex%d.png' % (i,index))
        image.save(png)
        resources['Resources'][str(index)] = str(png)
        t['type'] = 'RGBA8'
    extra_offsets, extra_lengths, extra_start = struct.unpack_from('<3I',blob,44)
    extra_offsets, extra_lengths = array(blob,extra_offsets), array(blob,extra_lengths)
    raw_dir=out / ('extra%d' % i)
    raw_dir.mkdir(exist_ok=True)
    resources['ExtraResources']={}
    resources.pop('ExtraFlattenArrays',None)
    for k,(offset,length) in enumerate(zip(extra_offsets,extra_lengths)):
        target=raw_dir / ('%d.bin' % k)
        target.write_bytes(blob[extra_start+offset:extra_start+offset+length])
        resources['ExtraResources']['@%d'%k]=str(target)
    (out / ('kurisu%d.json' % i)).write_text(json.dumps(data, ensure_ascii=False), 'utf-8')
    (out / ('kurisu%d.resx.json' % i)).write_text(json.dumps(resources, ensure_ascii=False), 'utf-8')
    print('Prepared', i, name)
