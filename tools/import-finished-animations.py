"""Build runtime PNGs and mirrored variants from the user's selected exports.

Reads the pre-normalized action folders in natural filename order.
Loop endpoints are scored using lower-body pose and motion continuity.
"""
from pathlib import Path
import io, json, re, zipfile
import numpy as np
import cv2
from PIL import Image, ImageOps, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/characters/成品序列帧'
OUT = ROOT / 'prototype/Assets/Finished'
QA = ROOT / 'artifacts/animation-import'
DEFS = {
 'idle': ('呼吸陪伴', False, 'loop'), 'stretch': ('伸懒腰', False, 'once'),
 'wave': ('偷偷招手', False, 'once'), 'curious': ('好奇歪头', False, 'once'),
 'proud': ('得意摇尾', False, 'once'), 'sleep': ('打瞌睡', False, 'once'),
 'peek': ('探头偷看', False, 'once'), 'tidy': ('整理围裙', False, 'once'),
 'notice': ('盯上指针', True, 'once'), 'grab': ('抓住准备拖', True, 'once'),
 'pull': ('用力拖拽', True, 'loop'), 'pickup': ('弯腰抱起', True, 'once'),
 'putdown': ('轻轻放下', True, 'once'), 'brake': ('跑步急刹', True, 'once'),
 'walk': ('小步走路', True, 'phased'), 'carry_idle': ('抱着等待', True, 'loop'),
 'carry_walk': ('抱着走路', True, 'phased'), 'run': ('急急忙忙跑', True, 'phased'),
 'sit': ('坐下', True, 'phased'), 'sit_idle': ('坐着摆腿', True, 'loop'),
 'yawn': ('打哈欠', False, 'once'), 'lie_sleep': ('趴下睡觉', True, 'phased'),
 'lifted': ('拎起来', True, 'loop'),
}

def natural(s): return [int(t) if t.isdigit() else t for t in re.split(r'(\d+)', str(s))]

def fit_square(im, size):
    # Preserve the original aspect ratio. Portrait exports must not be squashed.
    w,h=im.size; scale=size/max(w,h)
    fitted=im.resize((round(w*scale),round(h*scale)),Image.Resampling.LANCZOS)
    canvas=Image.new('RGBA',(size,size))
    canvas.alpha_composite(fitted,((size-fitted.width)//2,(size-fitted.height)//2))
    return canvas

def square_point(x,y,size):
    w,h=size; extent=max(w,h)
    return [(w*x+(extent-w)/2)/extent,(h*y+(extent-h)/2)/extent]

def seat_contact(im):
    # In the seated artwork the lowest central white skirt frill is the actual
    # supporting surface. Exclude dangling feet (left) and hair/tail (right).
    a=np.asarray(fit_square(im,384));rgb=a[:,:,:3].astype(float)
    maximum=rgb.max(2);minimum=rgb.min(2);yy,xx=np.indices((384,384))
    # Support the pelvis, not the long frill at the back of the dress.
    hem=(a[:,:,3]>180)&(maximum>160)&((maximum-minimum)<55)&(xx>166)&(xx<187)&(yy>288)&(yy<325)
    ys,xs=np.where(hem)
    if len(xs)<15:raise ValueError('Cannot identify seated skirt contact')
    bottom=np.percentile(ys,98)
    return [float(np.median(xs[ys>=bottom-2]))/384,(float(bottom)+1)/384]

def character_height(frames):
    heights=[]
    for im in frames[:6]:
        alpha=np.asarray(fit_square(im,384))[:,:,3]
        _,_,stats,_=cv2.connectedComponentsWithStats((alpha>128).astype('uint8'),8)
        body=stats[1:][np.argmax(stats[1:,4])]
        heights.append(int(body[3]))
    return float(np.median(heights))/384

def sleep_contact(im):
    a=np.asarray(im);r,g,b=a[:,:,:3].astype(float).transpose(2,0,1);h,w=a.shape[:2];yy,xx=np.indices((h,w))
    hands=(a[:,:,3]>180)&(r>180)&(r>b+25)&(r>g+8)&(g>95)&(xx>w*.22)&(xx<w*.4)&(yy>h*.49)&(yy<h*.63)
    ys,xs=np.where(hands)
    if len(xs)<20:raise ValueError('Cannot identify sleeping hand contact')
    bottom=np.percentile(ys,99)
    return square_point(float(np.median(xs[ys>=bottom-3]))/w,(float(bottom)+1)/h,im.size)

def read_source(name, processed=True):
    folder = SOURCE / name
    if processed and name=='偷偷招手':
        clean=ROOT/'assets/characters/processed/偷偷招手'
        if clean.is_dir():folder=clean
    if folder.is_dir():
        paths = {str(p.relative_to(folder)).replace('\\', '/'): p for p in folder.rglob('*') if p.is_file()}
        read = lambda k: paths[k.replace('\\', '/')].read_bytes()
    else:
        archive = zipfile.ZipFile(SOURCE / (name + '.zip'))
        paths = {k.replace('\\', '/'): k for k in archive.namelist() if not k.startswith('__MACOSX/')}
        read = lambda k: archive.read(paths[k.replace('\\', '/')])
    names = sorted((k for k in paths if k.endswith('.png')), key=natural)
    frames = [Image.open(io.BytesIO(read(n))).convert('RGBA') for n in names]
    return frames, names, 12

def find_loop(frames):
    # A repeated lower-body pose matters more than slowly drifting hair/tail.
    arrays = []
    for im in frames:
        a = np.asarray(im.resize((96,96)), dtype=np.float32) / 255
        a[:,:,:3] *= a[:,:,3:4]
        arrays.append(a)
    a = np.stack(arrays)
    weights = np.ones((96,96,1),np.float32) * .25
    weights[61:88,24:59] = 3
    candidates = []
    n = len(frames)
    for start in range(9, min(26,n//2)):
        for end in range(start+9, min(start+27,n-8)):
            pose = np.mean((a[start]-a[end])**2*weights)
            velocity = np.mean(((a[start+1]-a[start])-(a[end+1]-a[end]))**2*weights)
            activity = np.mean(np.var(a[start:end,61:88,24:59],axis=0))
            if activity < .001: continue
            candidates.append((float(pose+.3*velocity),start,end-1))
    if not candidates: raise ValueError('No active gait cycle detected')
    candidates.sort()
    return candidates[0], candidates[:8]

def main():
    OUT.mkdir(parents=True,exist_ok=True);QA.mkdir(parents=True,exist_ok=True)
    clips={};analysis={};font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',14)
    for key,(name,directional,mode) in DEFS.items():
        frames,names,fps = read_source(name)
        n=len(frames); loop_start=0;loop_end=n-1
        if key in ('walk','carry_walk','run'):
            best,candidates=find_loop(frames); _,loop_start,loop_end=best
            analysis[key]={'selected':best,'candidates':candidates}
        if key=='sit': loop_start,loop_end=24,38
        if key=='carry_idle': loop_start,loop_end=10,n-7
        if key=='pull': loop_start,loop_end=10,43
        if key=='lie_sleep': loop_start,loop_end=30,n-1
        # Foot anchors are stable per clip; bounce remains in the artwork.
        a=np.asarray(frames[0]); yy,xx=np.indices(a.shape[:2]); mask=(a[:,:,3]>180)
        body=mask&(xx>a.shape[1]*.25)&(xx<a.shape[1]*(.57 if directional else .67))
        bottom=float(np.percentile(yy[body],99.8))/a.shape[0]
        feet=body&(yy>a.shape[0]*(bottom-.045))
        foot_x=float(np.median(xx[feet]))/a.shape[1]
        pivot=[round(v,4) for v in square_point(foot_x,bottom,frames[0].size)]
        if key=='sit_idle': pivot=seat_contact(frames[0])
        if key=='lifted': pivot=[.47,.40]
        # Anchor keyframes move the seat contact from feet to hips and back.
        anchors=[]
        if key in ('sit','sit_idle'):
            indices=range(24,42) if key=='sit' else range(n)
            contacts=np.array([seat_contact(frames[i]) for i in indices])
            smoothed=np.array([np.median(contacts[max(0,j-2):min(len(contacts),j+3)],axis=0) for j in range(len(contacts))])
            anchors=[{'frame':i,'x':round(float(p[0]),5),'y':round(float(p[1]),5)} for i,p in zip(indices,smoothed)]
            if key=='sit':anchors=[{'frame':0,'x':pivot[0],'y':pivot[1]},{'frame':10,'x':pivot[0],'y':pivot[1]}]+anchors+[{'frame':n-1,'x':pivot[0],'y':pivot[1]}]
        if key=='lie_sleep':
            contacts=np.array([sleep_contact(frames[i]) for i in range(18,n)])
            smoothed=np.array([np.median(contacts[max(0,j-2):min(len(contacts),j+3)],axis=0) for j in range(len(contacts))])
            anchors=[{'frame':0,'x':pivot[0],'y':pivot[1]},{'frame':6,'x':pivot[0],'y':pivot[1]}]+[{'frame':i+18,'x':round(float(p[0]),5),'y':round(float(p[1]),5)} for i,p in enumerate(smoothed)]
        reference_height=character_height(frames)
        if key=='sit_idle':reference_height=clips['sit']['referenceHeight']
        clips[key]={'label':name,'count':n,'fps':fps,'directional':directional,'mode':mode,'loopStart':loop_start,'loopEnd':loop_end,'pivotX':pivot[0],'pivotY':pivot[1],'anchors':anchors,'sourceWidth':frames[0].width,'sourceHeight':frames[0].height,'fit':'contain','referenceHeight':reference_height,'displayScale':.8/reference_height,'sources':names}
        left=OUT/key/'left';left.mkdir(parents=True,exist_ok=True)
        right=OUT/key/'right'
        if directional:right.mkdir(parents=True,exist_ok=True)
        for i,im in enumerate(frames):
            small=fit_square(im,384)
            small.save(left/f'{i:04}.png')
            if directional:ImageOps.mirror(small).save(right/f'{i:04}.png')
        if mode=='phased' or key=='sit_idle':
            sheet=Image.new('RGB',(1200,((n+7)//8)*165),'#e7eef5');draw=ImageDraw.Draw(sheet)
            for i,im in enumerate(frames):
                thumb=fit_square(im,145);x=i%8*150;y=i//8*165
                sheet.paste(thumb,(x,y),thumb);draw.text((x+5,y+145),f'{i}'+(' LOOP' if loop_start<=i<=loop_end else ''),font=font,fill='#11665b' if loop_start<=i<=loop_end else '#333333')
            sheet.save(QA/f'{key}-frames.jpg',quality=90)
            gif=[]
            for i in range(loop_start,loop_end+1):
                bg=Image.new('RGBA',(256,256),'#e7eef5');bg.alpha_composite(fit_square(frames[i],256));gif.append(bg.convert('RGB'))
            gif[0].save(QA/f'{key}-loop.gif',save_all=True,append_images=gif[1:],duration=round(1000/fps),loop=0)
        print(f'{key}: {n}, loop {loop_start}..{loop_end}',flush=True)
    manifest={'version':1,'renderSize':128,'characterHeight':102.4,'source':'assets/characters/成品序列帧','defaultFps':12,'clips':clips}
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    (QA/'loop-analysis.json').write_text(json.dumps(analysis,indent=2),encoding='utf-8')

if __name__=='__main__': main()

