import json, sys, math
import numpy as np
rows = []
for f in sys.argv[1:]:
    rows += [json.loads(l) for l in open(f, encoding="utf-8") if l.strip()]
def split(pu):
    seis = [v for k, v in pu.items() if "RS" in k]; grav = [v for k, v in pu.items() if "RS" not in k]
    return (max(seis) if seis else 0, max(grav) if grav else 0, max(pu.values()) if pu else 0)
def lim(root, Ca, high=True):
    if Ca <= 0.114: return (2.57*(1-1.04*Ca) if high else 3.96*(1-3.04*Ca))*root
    return max((0.88*(2.68-Ca)) if high else 1.29*(2.12-Ca), 1.57)*root
def lbok(r):
    lb = r["ltb"] if r["ltb"] > 0 else r["len"]-r["offI"]-r["offJ"]
    return lb/r["r22"] <= 0.095*r["E"]/(1.1*r["Fy"])
data = []
for r in rows:
    if "etErr" not in r: continue
    err = r["etErr"]
    et_comp = "seismically compact" in err
    et_lb = "Lb/ry" in err
    mom = r["col"] == 1 or not any(c == "1" for c in (r["relI"][4:6] + r["relJ"][4:6]))
    root = math.sqrt(r["E"]/(1.1*r["Fy"]))
    fl = r["bf"]/(2*r["tf"])/root/0.32
    h = r["d"]-2*(r["tf"]+r["fil"])
    wr = h/r["tw"]/root
    pu = split(r["pu"])
    hasmom = r["col"] == 0 or any(b["mom"] == 1 for b in r.get("beams", [])) or r.get("above", -1) < 0
    data.append(dict(r=r, comp=et_comp, lb=et_lb, mom=mom, fl=fl, wr=wr, pu=pu, Py=r["A"]*r["Fy"], hasmom=hasmom, col=r["col"]))
print("members", len(data), "ETABS compact errors", sum(d["comp"] for d in data), "Lb errors", sum(d["lb"] for d in data))
# Lb/ry
c = {"both":0,"E":0,"F":0}
for d in data:
    if d["col"] or not d["mom"]: continue
    f = not lbok(d["r"])
    c["both" if f and d["lb"] else "E" if d["lb"] else "F" if f else "n"] = c.get("both" if f and d["lb"] else "E" if d["lb"] else "F" if f else "n",0)+1
print("Lb/ry beams:", c)
# compactness: confusion for Ca candidate definitions
def pred(d, ca, onlymom):
    if onlymom and not d["mom"]: return False
    if d["col"] and onlymom and not d["hasmom"]: return False
    if d["fl"] > 1.0: return True
    return d["wr"] > lim(1.0, ca) / 1.0
def conf(name, cafun, onlymom):
    tp=fp=fn=tn=0
    for d in data:
        if d["lb"] and not d["comp"]: continue
        p = pred(d, cafun(d), onlymom); e = d["comp"]
        tp += p and e; fp += p and not e; fn += (not p) and e; tn += (not p) and not e
    print("  %-40s FEM+ETABS+ %d  FEM only %d  ETABS only %d  neither %d" % (name, tp, fp, fn, tn))
for onlymom in (False, True):
    print("moment-frame restriction (columns need moment beam):", onlymom)
    for k, nm in ((0,"Ca seismic max"),(1,"Ca gravity max"),(2,"Ca all max")):
        for scale in (0.0, 0.5, 1.0, 1.25):
            conf("%s x %.2f" % (nm, scale), lambda d, k=k, s=scale: s*d["pu"][k]/(0.9*d["Py"]), onlymom)
# flange-only check: flagged beams/cols with web comfortable
print("flange ratio of ETABS-flagged members (web comfortable):")
fl_f = sorted(d["fl"] for d in data if d["comp"] and d["wr"] < 1.5)
fl_n = sorted(d["fl"] for d in data if not d["comp"] and d["mom"])
print("  flagged min %.3f max %.3f; not flagged (moment members) max %.3f" % (fl_f[0] if fl_f else 0, fl_f[-1] if fl_f else 0, fl_n[-1] if fl_n else 0))
print("\n--- detailed mismatch analysis (moment restriction, Ca seismic x s)")
def run(s, show=False):
    fo=eo=0; rows_=[]
    for d in data:
        if d["lb"] and not d["comp"]: continue
        p = pred(d, s*d["pu"][0]/(0.9*d["Py"]), True); e=d["comp"]
        if p and not e: fo+=1; rows_.append(("FEMonly",d))
        if e and not p: eo+=1; rows_.append(("ETABSonly",d))
    return fo,eo,rows_
best=None
for s in np.arange(0.6,3.01,0.1):
    fo,eo,_=run(s); print("  scale %.1f: FEM only %d ETABS only %d total %d"%(s,fo,eo,fo+eo))
fo,eo,rows_=run(1.0)
seen=set()
for k,d in rows_:
    key=(k,d["r"]["sec"],d["col"],round(d["pu"][0]/(0.9*d["Py"]),3))
    if key in seen: continue
    seen.add(key)
    if len(seen)>30: break
    print("  %-9s %s %s Ca %.3f fl %.3f wr %.3f crit-lim %.3f"%(k,d["r"]["sec"],"col" if d["col"] else "beam",d["pu"][0]/(0.9*d["Py"]),d["fl"],d["wr"],lim(1.0,d["pu"][0]/(0.9*d["Py"]))))
