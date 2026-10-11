"""Generate Luma's original geometric app mark; no stock artwork."""
from pathlib import Path
from PIL import Image, ImageDraw
target=Path(__file__).resolve().parents[1]/'src'/'Luma.App'/'Assets'
target.mkdir(parents=True,exist_ok=True)
canvas=Image.new('RGBA',(1024,1024));mask=Image.new('L',(1024,1024));ImageDraw.Draw(mask).rounded_rectangle((24,24,1000,1000),radius=240,fill=255)
gradient=Image.new('RGBA',(1024,1024));pixels=gradient.load()
for y in range(1024):
    for x in range(1024):
        t=(x+y)/2048;pixels[x,y]=(int(15+8*t),int(164-55*t),int(253-12*t),255)
canvas.paste(gradient,(0,0),mask);d=ImageDraw.Draw(canvas)
d.rounded_rectangle((232,258,792,766),radius=90,outline='white',width=42)
d.ellipse((566,360,656,450),fill='white')
d.line([(242,664),(388,495),(513,636),(608,546),(780,716)],fill='white',width=43,joint='curve')
for filename,size in [('Square44x44Logo.png',44),('Square150x150Logo.png',150),('StoreLogo.png',50),('Logo256.png',256)]:
    canvas.resize((size,size),Image.Resampling.LANCZOS).save(target/filename)
canvas.resize((256,256),Image.Resampling.LANCZOS).save(target/'Luma.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
