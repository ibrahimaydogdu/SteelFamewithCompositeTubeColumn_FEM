import json, sys, numpy as np
rows=[]
for f in sys.argv[1:]: rows+=[json.loads(l) for l in open(f,encoding="utf-8") if l.strip()]
def num(s):
    try: return float(s)
    except: return None
def seis(pu):
    v=[x for k,x in pu.items() if "RS" in k]; return max(v) if v else 0
X=[];Y=[];info=[]
for r in rows:
    if not r["col"] or "etBC" not in r or "ab" not in r: continue
    if "seismically" in r.get("etErr","") or "Lb/ry" in r.get("etErr",""): continue
    for pl in (0,1):
        bs=[b for b in r["beams"] if b["mom"]==1 and ((b["al2"]>0.7)==(pl==0))]
        if not bs: continue
        ev=num(r["etBC"][pl])
        Py=r["A"]*r["Fy"]; Pya=r["ab"]["A"]*r["ab"]["Fy"]
        feats=dict(caC=seis(r["pu"])/Py, caA=seis(r["ab"]["pu"])/Pya, femBC=r["femBC"][pl], pmm=r.get("etRatio",0), pl=pl, nb=len(bs), z=r["Z33"] if pl==0 else r["Z22"], za=r["ab"]["Z33"] if pl==0 else r["ab"]["Z22"], zb=sum(b["Z33"] for b in bs), seed=r["seed"], f=r["f"], sec=r["sec"], asec=r["ab"]["sec"])
        Y.append(ev is None); info.append(feats)
Y=np.array(Y); print("points",len(Y),"blank",Y.sum())
for k in ("caC","caA","femBC","pmm","z","za","zb","nb","pl"):
    a=np.array([i[k] for i in info]); print("%-6s blank: %.3f..%.3f (med %.3f)  numeric: %.3f..%.3f (med %.3f)"%(k,a[Y].min(),a[Y].max(),np.median(a[Y]),a[~Y].min(),a[~Y].max(),np.median(a[~Y])))
# group by (seed, frame): is blank all-or-nothing per column?
from collections import defaultdict
g=defaultdict(list)
for y,i in zip(Y,info): g[(i["seed"],i["f"])].append(y)
print("columns with both planes available:",sum(len(v)==2 for v in g.values()),"mixed:",sum(len(v)==2 and v[0]!=v[1] for v in g.values()))
# test: blank if caC > t or pmm > t etc, accuracy
for k in ("caC","caA","pmm","femBC"):
    a=np.array([i[k] for i in info]); best=max(((np.mean((a>t)==Y),t) for t in np.unique(a)))
    print("threshold on %s: best accuracy %.3f at %.3f"%(k,best[0],best[1]))
# maybe blank depends on above column section class? print few examples
for y,i in list(zip(Y,info))[:0]: pass
