# MD Dia-Inch icons - Author: MD (Mayank Dholariya)
from PIL import Image, ImageDraw, ImageFont
TEAL=(0,100,125); WHITE=(255,255,255); ORANGE=(245,158,40); PIPE=(225,236,242); DARK=(20,60,75)

def draw(S):
    k=4; s=S*k
    im=Image.new('RGBA',(s,s),(0,0,0,0)); d=ImageDraw.Draw(im)
    d.rounded_rectangle([0,0,s-1,s-1],radius=int(s*0.18),fill=TEAL)
    # two pipe spools meeting at a weld, then an elbow up
    h=int(s*.20); y=int(s*.58)
    d.rectangle([int(s*.08),y-h//2,int(s*.62),y+h//2],fill=PIPE)
    d.rectangle([int(s*.62)-h//2,int(s*.16),int(s*.62)+h//2,y+h//2],fill=PIPE)
    # weld rings
    w=max(k*2,int(s*.06))
    for x in (int(s*.34),):
        d.rectangle([x-w//2,y-h//2-int(s*.03),x+w//2,y+h//2+int(s*.03)],fill=ORANGE)
    yy=int(s*.36)
    d.rectangle([int(s*.62)-h//2-int(s*.03),yy-w//2,int(s*.62)+h//2+int(s*.03),yy+w//2],fill=ORANGE)
    # diameter mark (Ø) bottom right
    if S>=32:
        c=(int(s*.82),int(s*.80)); r=int(s*.10)
        d.ellipse([c[0]-r,c[1]-r,c[0]+r,c[1]+r],outline=WHITE,width=max(k,int(s*.03)))
        d.line([c[0]-r-int(s*.03),c[1]+r+int(s*.03),c[0]+r+int(s*.03),c[1]-r-int(s*.03)],fill=WHITE,width=max(k,int(s*.03)))
    return im.resize((S,S),Image.LANCZOS)

for S in (16,32,256):
    draw(S).save('MDDiaInch_%d.png'%S)
draw(256).save('MDDiaInch.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])

def font(sz,bold=False):
    try: return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf'%('-Bold' if bold else ''),sz)
    except Exception: return ImageFont.load_default()

L=Image.new('RGB',(164,314),TEAL); d=ImageDraw.Draw(L)
for y in range(314):
    c=tuple(int(TEAL[i]*(1-y/314*.45)) for i in range(3)); d.line([(0,y),(164,y)],fill=c)
L.paste(draw(110),(27,40),draw(110))
d.text((82,180),"MD Dia-Inch",fill=WHITE,font=font(17,True),anchor='mm')
d.text((82,204),"weld joints, inch-dia",fill=(215,235,240),font=font(11),anchor='mm')
d.text((82,222),"for Navisworks",fill=(215,235,240),font=font(11),anchor='mm')
d.text((82,296),"MD (Mayank Dholariya)",fill=(190,215,225),font=font(9),anchor='mm')
L.save('wizard_large.bmp')
Sm=Image.new('RGB',(55,55),WHITE); Sm.paste(draw(48),(3,3),draw(48)); Sm.save('wizard_small.bmp')
