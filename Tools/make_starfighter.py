# Builds Graphics/Models/Starfighter.obj and its texture Starfighter.png (needs Pillow)
# Run from the project folder: python3 Tools/make_starfighter.py Graphics/Models/Starfighter
import math, sys
from PIL import Image, ImageDraw, ImageFilter
out = sys.argv[1]

V=[]; VT=[]; F=[]
def sub(a,b): return (a[0]-b[0],a[1]-b[1],a[2]-b[2])
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def dot(a,b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def avg(ps): n=len(ps); return tuple(sum(p[i] for p in ps)/n for i in range(3))

def face(pts, uvs, hint):
    """pts: corners of a flat face; turned so it goes anticlockwise seen from the hint side"""
    n = cross(sub(pts[1],pts[0]), sub(pts[2],pts[0]))
    if dot(n, sub(avg(pts), hint)) < 0: pts = pts[::-1]; uvs = uvs[::-1]
    idx=[]
    for p,t in zip(pts,uvs):
        V.append(p); VT.append(t); idx.append(len(V))
    F.append(idx)

# Texture regions (u0,v0,u1,v1) in 0..1, v from the top of the picture
HULL=(0.02,0.02,0.48,0.48); DARK=(0.52,0.02,0.98,0.48); GLASS=(0.02,0.52,0.48,0.98)
GLOW=(0.52,0.52,0.73,0.73); RED=(0.77,0.52,0.98,0.73)
def uv(r,s,t): return (r[0]+(r[2]-r[0])*s, 1-(r[1]+(r[3]-r[1])*t))   # obj v goes up

def loft(rings, region, caps=(True,True), capregion=None):
    """rings: lists of points with the same count; faces between neighbours, caps at the ends"""
    n=len(rings[0]); cs=[avg(r) for r in rings]
    for i in range(len(rings)-1):
        for k in range(n):
            a,b=rings[i][k],rings[i][(k+1)%n]; c,d=rings[i+1][(k+1)%n],rings[i+1][k]
            s0,s1=k/n,(k+1)/n; t0,t1=i/(len(rings)-1),(i+1)/(len(rings)-1)
            pts=[a,b,c,d]; uvs=[uv(region,s0,t0),uv(region,s1,t0),uv(region,s1,t1),uv(region,s0,t1)]
            # drop a corner where two meet (a point at the nose)
            keep=[(p,u) for j,(p,u) in enumerate(zip(pts,uvs)) if all(abs(p[x]-pts[j-1][x])>1e-6 for x in [0]) or sub(p,pts[j-1])!=(0,0,0)]
            keep=[(p,u) for j,(p,u) in enumerate(zip(pts,uvs)) if sub(p,pts[j-1])!=(0.0,0.0,0.0)]
            if len(keep)>=3: face([p for p,_ in keep],[u for _,u in keep], avg([cs[i],cs[i+1]]))
    cr=capregion or region
    for end,(ring,inner) in zip(caps,[(rings[0],cs[1]),(rings[-1],cs[-2])]):
        if not end: continue
        c=avg(ring)
        if max(abs(x) for x in sub(ring[0],c))<1e-6: continue
        uvs=[uv(cr,0.5+0.5*math.cos(2*math.pi*k/n),0.5+0.5*math.sin(2*math.pi*k/n)) for k in range(n)]
        face(list(ring),uvs,inner)

def ring_z(z,w,h,cx=0,cy=0,sides=8,flat=1.0):
    pts=[]
    for k in range(sides):
        a=2*math.pi*(k+0.5)/sides
        pts.append((cx+math.cos(a)*w/2, cy+math.sin(a)*h/2*(flat if math.sin(a)<0 else 1), z))
    return pts

# Fuselage, the nose at +z
body=[(-62,24,16),(-45,30,22),(-10,32,24),(30,26,19),(70,15,11),(105,4,4),(118,0.01,0.01)]
loft([ring_z(z,w,h,0,0,8,0.8) for z,w,h in body],HULL,caps=(True,False),capregion=DARK)
# Canopy on top
can=[(-12,3,0.5),(0,14,8),(25,15,9),(50,9,5),(64,0.5,0.5)]
def half(z,w,h):
    pts=[]
    for k in range(7):
        a=math.pi*k/6; pts.append((math.cos(a)*w/2, 9+math.sin(a)*h, z))
    return pts
loft([half(*c) for c in can],GLASS)
# Wings: lofts along x, the cross section a thin diamond in z and y
def wing_ring(x,z0,z1,th,y):
    zm=z0+(z1-z0)*0.35
    return [(x,y,z0),(x,y+th/2,zm),(x,y,z1),(x,y-th/2,zm)]
for s in (1,-1):
    wing=[wing_ring(s*12,-50,35,7,-3),wing_ring(s*60,-48,0,4,-7),wing_ring(s*104,-44,-22,2,-11)]
    loft(wing,HULL)
    # gun on the wing tip
    loft([ring_z(z,4,4,s*106,-11,6) for z in (-50,40)],DARK)
    loft([ring_z(z,w,w,s*106,-11,6) for z,w in ((40,2),(52,1))],RED)
    # red band on the wing
    band=[wing_ring(s*70,-47.2,-2,3.8,-7.2),wing_ring(s*82,-46.2,-9,3.2,-8.2)]
    loft(band,RED,caps=(False,False))
    # engine pod
    eng=[(-74,15),(-66,20),(-30,20),(-10,14)]
    loft([ring_z(z,w,w,s*24,-2,10) for z,w in eng],DARK,caps=(True,True),capregion=GLOW)
# Tail fin: a loft along y
def fin_ring(y,z0,z1,th):
    zm=z0+(z1-z0)*0.4
    return [(0,y,z0),(th/2,y,zm),(0,y,z1),(-th/2,y,zm)]
loft([fin_ring(9,-60,-18,5),fin_ring(42,-64,-48,2)],HULL)
loft([fin_ring(42,-64,-48,2),fin_ring(46,-65,-52,1.6)],RED)

with open(out+".obj","w") as f:
    f.write("# Starfighter for AMOS Like Basic: made in code, free to use\n")
    f.write("# The nose points along +z, y is up. %d faces.\n"%len(F))
    for v in V: f.write("v %.3f %.3f %.3f\n"%v)
    for t in VT: f.write("vt %.4f %.4f\n"%t)
    for fc in F: f.write("f "+" ".join("%d/%d"%(i,i) for i in fc)+"\n")
print(len(V),"corners",len(F),"faces")

# The texture, 256 x 256
S=256; im=Image.new("RGB",(S,S),(40,40,48)); d=ImageDraw.Draw(im)
def box(r): return [int(r[0]*S),int(r[1]*S),int(r[2]*S)-1,int(r[3]*S)-1]
# hull: light grey panels with lines and rivets
b=box(HULL); d.rectangle(b,fill=(196,200,208))
import random; random.seed(7)
for i in range(18):
    x=random.randint(b[0],b[2]-20); y=random.randint(b[1],b[3]-14)
    g=random.randint(176,214); d.rectangle([x,y,x+random.randint(12,40),y+random.randint(8,24)],fill=(g,g+2,g+8))
for x in range(b[0],b[2],16): d.line([x,b[1],x,b[3]],fill=(120,124,134))
for y in range(b[1],b[3],20): d.line([b[0],y,b[2],y],fill=(120,124,134))
# dark metal with ribs
b=box(DARK); d.rectangle(b,fill=(70,74,84))
for y in range(b[1],b[3],8): d.rectangle([b[0],y,b[2],y+2],fill=(52,55,64))
# glass: blue with a light streak
b=box(GLASS)
for y in range(b[1],b[3]+1):
    t=(y-b[1])/(b[3]-b[1]); d.line([b[0],y,b[2],y],fill=(int(30+60*t),int(80+90*t),int(150+90*t)))
d.polygon([(b[0]+20,b[1]),(b[0]+44,b[1]),(b[0]+14,b[3]),(b[0]-0,b[3])],fill=(190,225,255))
# engine glow
b=box(GLOW); cx=(b[0]+b[2])/2; cy=(b[1]+b[3])/2; R=(b[2]-b[0])/2
for r in range(int(R),0,-1):
    t=r/R; d.ellipse([cx-r,cy-r,cx+r,cy+r],fill=(255,int(250-150*t),int(220-200*t)))
# red
b=box(RED); d.rectangle(b,fill=(200,30,36)); d.rectangle([b[0],b[1]+20,b[2],b[1]+26],fill=(240,220,60))
im.save(out+".png")
