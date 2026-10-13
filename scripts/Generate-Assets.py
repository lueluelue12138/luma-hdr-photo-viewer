"""Generate Windows icon sizes from the maintainer-provided transparent logo."""
from pathlib import Path
from PIL import Image
import argparse

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--source',type=Path,default=root/'branding/Luma.png')
args=parser.parse_args()
target=root/'src/Luma.App/Assets'
canvas=Image.open(args.source)
canvas.load()
if canvas.mode!='RGBA' or canvas.width!=canvas.height:
    raise ValueError('Use the square transparent RGBA master logo.')
target.mkdir(parents=True,exist_ok=True)
save_options={'optimize':True,'compress_level':9}
if 'icc_profile' in canvas.info:save_options['icc_profile']=canvas.info['icc_profile']
for filename,size in [('Square44x44Logo.png',44),('Square150x150Logo.png',150),('StoreLogo.png',50),('Logo256.png',256)]:
    derived=canvas.resize((size,size),Image.Resampling.LANCZOS)
    derived.save(target/filename,**save_options)
    check=Image.open(target/filename)
    assert check.mode=='RGBA' and check.size==(size,size) and check.tobytes()==derived.tobytes()
    assert check.getchannel('A').getextrema()==(0,255)
canvas.resize((256,256),Image.Resampling.LANCZOS).save(target/'Luma.ico',sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(96,96),(128,128),(256,256)])
icon=Image.open(target/'Luma.ico')
assert icon.ico.sizes()=={(n,n) for n in [16,20,24,32,40,48,64,96,128,256]}
print('Generated 4 losslessly encoded transparent PNG sizes and 10 Windows ICO sizes from branding/Luma.png.')
