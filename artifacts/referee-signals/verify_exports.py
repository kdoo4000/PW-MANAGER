from pathlib import Path
from PIL import Image
import json, zipfile
root = Path('C:/Projects/PWMANAGER/artifacts/referee-signals')
ref = Image.open(root.parent / 'referee-walk/REF.png').convert('RGBA')
skin = {p[:3] for p in ref.getdata() if p[3] and p[0] > 180 and p[0] > p[1]+12 and p[1] > p[2]+3}
manifest = {'cell_size': [64,64], 'reference': 'REF.png', 'actions': []}
files = []
for name, count in [('Standing_Count',7),('Ring_Bell_Signal',7),('Stop_Match',6)]:
    im = Image.open(root / (name+'.png')).convert('RGBA')
    assert im.size == (64*count,64)
    used = {p[:3] for p in im.getdata() if p[3] and p[0] > 180 and p[0] > p[1]+12 and p[1] > p[2]+3}
    assert used <= skin, (name, used-skin)
    assert {p[3] for p in im.getdata()} == {0,255}
    for f in range(count):
        bounds=im.crop((f*64,0,(f+1)*64,64)).getbbox()
        assert bounds and bounds[0]>0 and bounds[2]<64 and bounds[1]>0 and bounds[3]==48, (name,f,bounds)
    gif=Image.open(root/(name+'_Preview.gif'))
    durations=[]
    for f in range(gif.n_frames):
        gif.seek(f); durations.append(gif.info['duration'])
    assert sum(durations)==1100, (name,durations)
    manifest['actions'].append({'name':name,'frames':count,'duration_ms':1100,'png':name+'.png','source':name+'.aseprite'})
    files += [root/(name+ext) for ext in ['.aseprite','.png','_Preview.gif']]
(root/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
files += [root/'manifest.json',root/'Referee_Signals_Preview.gif']
with zipfile.ZipFile(root/'Referee_Signals.zip','w',zipfile.ZIP_DEFLATED) as archive:
    for path in files: archive.write(path,path.name)
print('PASS: 3 actions, 20 frames, original skin palette, binary transparency, y47 contact line, 1100ms per action. ZIP created.')
