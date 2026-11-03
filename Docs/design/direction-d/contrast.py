import math, sys

def h2rgb(h):
    h = h.lstrip('#'); return tuple(int(h[i:i+2], 16) / 255 for i in (0, 2, 4))

def lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def lum(h):
    r, g, b = (lin(c) for c in h2rgb(h)); return 0.2126*r + 0.7152*g + 0.0722*b

def cr(a, b):
    la, lb = lum(a), lum(b); hi, lo = max(la, lb), min(la, lb); return (hi + .05) / (lo + .05)

def lab(h):
    r, g, b = (lin(c) for c in h2rgb(h))
    X = (0.4124564*r + 0.3575761*g + 0.1804375*b) / 0.95047
    Y = (0.2126729*r + 0.7151522*g + 0.0721750*b)
    Z = (0.0193339*r + 0.1191920*g + 0.9503041*b) / 1.08883
    f = lambda t: t ** (1/3) if t > 216/24389 else (24389/27*t + 16) / 116
    fx, fy, fz = f(X), f(Y), f(Z)
    return 116*fy - 16, 500*(fx - fy), 200*(fy - fz)

def lch(h):
    L, a, b = lab(h); return L, math.hypot(a, b), math.degrees(math.atan2(b, a)) % 360

def de00(h1, h2):
    L1, a1, b1 = lab(h1); L2, a2, b2 = lab(h2)
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2); Cb = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cb**7 / (Cb**7 + 25**7)))
    a1p, a2p = a1*(1+G), a2*(1+G)
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360; h2p = math.degrees(math.atan2(b2, a2p)) % 360
    dL = L2 - L1; dC = C2p - C1p
    dh = h2p - h1p
    if C1p*C2p == 0: dh = 0
    elif dh > 180: dh -= 360
    elif dh < -180: dh += 360
    dH = 2*math.sqrt(C1p*C2p)*math.sin(math.radians(dh/2))
    Lb = (L1+L2)/2; Cbp = (C1p+C2p)/2
    if C1p*C2p == 0: hb = h1p+h2p
    elif abs(h1p-h2p) <= 180: hb = (h1p+h2p)/2
    elif h1p+h2p < 360: hb = (h1p+h2p+360)/2
    else: hb = (h1p+h2p-360)/2
    T = 1-0.17*math.cos(math.radians(hb-30))+0.24*math.cos(math.radians(2*hb))+0.32*math.cos(math.radians(3*hb+6))-0.20*math.cos(math.radians(4*hb-63))
    dth = 30*math.exp(-((hb-275)/25)**2)
    Rc = 2*math.sqrt(Cbp**7/(Cbp**7+25**7))
    Sl = 1+0.015*(Lb-50)**2/math.sqrt(20+(Lb-50)**2); Sc = 1+0.045*Cbp; Sh = 1+0.015*Cbp*T
    Rt = -math.sin(math.radians(2*dth))*Rc
    return math.sqrt((dL/Sl)**2+(dC/Sc)**2+(dH/Sh)**2+Rt*(dC/Sc)*(dH/Sh))

if __name__ == '__main__':
    L = dict(page='#FFFFFF', band='#F5F6F8', desk='#E9EBEE', hover='#E6E8EC')
    D = dict(page='#1B1D21', band='#222429', desk='#131417', hover='#2C2F35')
    inksL = dict(write='#1D4F91', sheets='#1E6B47', slides='#A3420F', pdf=sys.argv[1] if len(sys.argv) > 1 else '#6E1F48', violet='#7B2FBE', red='#B3261E')
    inksD = dict(write='#8DB4EE', sheets='#7CCBA2', slides='#F0A374', pdf=sys.argv[2] if len(sys.argv) > 2 else '#E79AC4', violet='#B48CF0', red='#FF8A80')
    neutL = dict(text='#1A1D21', text2='#4A5059', ctl='#7A818B', rule='#D3D7DD')
    neutD = dict(text='#E8EAED', text2='#A9AFB8', ctl='#7D848E', rule='#373B42')
    for name, inks, surf, neut in (('LIGHT', inksL, L, neutL), ('DARK', inksD, D, neutD)):
        print(name)
        for k, v in list(inks.items()) + list(neut.items()):
            Lst, C, H = lch(v)
            print(f'  {k:7} {v}  L*={Lst:5.1f} C={C:5.1f} h={H:5.1f}  ' + '  '.join(f'{s}:{cr(v, c):5.2f}' for s, c in surf.items()))
        p = inks['pdf']
        print('  dE00 pdf vs', {k: round(de00(p, v), 1) for k, v in inks.items() if k != 'pdf'})
        print('  dE00 violet vs', {k: round(de00(inks['violet'], v), 1) for k, v in inks.items() if k != 'violet'})
        print('  dE00 red vs', {k: round(de00(inks['red'], v), 1) for k, v in inks.items() if k != 'red'})
        bg = surf['page'] if name == 'LIGHT' else surf['page']
        fg = '#FFFFFF' if name == 'LIGHT' else surf['page']
        print('  on-ink text', {k: round(cr(fg, v), 2) for k, v in inks.items()})
