import json, sys, math
import numpy as np
from scipy.optimize import least_squares

rows = [json.loads(l) for l in open(sys.argv[1], encoding="utf-8") if l.strip()]
print("members", len(rows), "columns", sum(r["col"] for r in rows))

def num(s):
    try:
        return float(s)
    except Exception:
        return None

def pu_split(pu):
    seis = [v for k, v in pu.items() if "RS" in k]
    grav = [v for k, v in pu.items() if "RS" not in k]
    return (max(seis) if seis else 0.0, max(grav) if grav else 0.0, max(pu.values()) if pu else 0.0)

# ---------- SCWB fit
pts = []
for r in rows:
    if not r["col"] or "ab" not in r or "etBC" not in r:
        continue
    for pl in (0, 1):
        ev = num(r["etBC"][pl])
        if ev is None or ev <= 0:
            continue
        bs = [b for b in r["beams"] if b["mom"] == 1 and ((b["al2"] > 0.7) == (pl == 0))]
        if not bs:
            continue
        pc = pu_split(r["pu"]); pa = pu_split(r["ab"]["pu"])
        pts.append(dict(ev=ev, pl=pl, bs=bs, zc=r["Z33"] if pl == 0 else r["Z22"], za=r["ab"]["Z33"] if pl == 0 else r["ab"]["Z22"],
                        Ac=r["A"], Aa=r["ab"]["A"], Fyc=r["Fy"], Fya=r["ab"]["Fy"], pc=pc, pa=pa, f=r["f"], seed=r["seed"]))
print("SCWB points", len(pts))

def model(par, mode):
    C0, C1 = par[0], par[1]
    out = []
    for p in pts:
        sb = sum(C0 * b["Fy"] * b["Z33"] * (1 + C1 * b["off"] / b["L"]) for b in p["bs"])
        if mode == "all":
            a = par[2]; uc = a * p["pc"][2]; ua = a * p["pa"][2]
        elif mode == "split":
            uc = par[2] * p["pc"][0] + par[3] * p["pc"][1]; ua = par[2] * p["pa"][0] + par[3] * p["pa"][1]
        else:
            uc = ua = 0.0
        sc = p["zc"] * (p["Fyc"] - uc / p["Ac"]) + p["za"] * (p["Fya"] - ua / p["Aa"])
        out.append(sb / sc if sc > 0 else 1e9)
    return np.array(out)

ev = np.array([p["ev"] for p in pts])
def fit(mode, x0):
    f = lambda par: np.log(np.maximum(model(par, mode), 1e-9) / ev)
    s = least_squares(f, x0)
    rel = model(s.x, mode) / ev - 1
    return s.x, rel
for mode, x0 in (("none", [1.5, 2.2]), ("all", [1.5, 2.2, 1.0]), ("split", [1.5, 2.2, 1.0, 1.0])):
    x, rel = fit(mode, x0)
    print("SCWB fit %-5s params %s  mean %+.2f%% rms %.2f%% max %.1f%% p95 %.1f%%" % (mode, np.round(x, 4), 100 * rel.mean(), 100 * np.sqrt((rel ** 2).mean()), 100 * np.abs(rel).max(), 100 * np.percentile(np.abs(rel), 95)))
cur = model([1.5335, 2.23, 1.0], "all")
rel = cur / ev - 1
print("current formula (C0 1.5335, C1 2.23, alpha 1 on max Pu): mean %+.2f%% rms %.2f%% max %.1f%%" % (100 * rel.mean(), 100 * np.sqrt((rel ** 2).mean()), 100 * np.abs(rel).max()))

# ---------- blank BC rule
print("\nBC blank analysis (columns without seismic error text)")
bl = []
for r in rows:
    if not r["col"] or "etBC" not in r or "ab" not in r:
        continue
    err = r.get("etErr", "")
    if "seismically" in err or "Lb/ry" in err:
        continue
    for pl in (0, 1):
        bs = [b for b in r["beams"] if b["mom"] == 1 and ((b["al2"] > 0.7) == (pl == 0))]
        if not bs:
            continue
        ev1 = num(r["etBC"][pl])
        pc = pu_split(r["pu"])
        Py = r["A"] * r["Fy"]
        bl.append((ev1 is None, pc[0] / (0.9 * Py), pc[1] / (0.9 * Py), pc[2] / (0.9 * Py), r["femBC"][pl], r["sec"], r["grp"], r.get("etRatio", 0), pl, len(r["beams"]), r["Z33"]))
nb = [x for x in bl if x[0]]
print("blank", len(nb), "of", len(bl))
for name, idx in (("Ca seismic", 1), ("Ca gravity", 2), ("Ca max", 3), ("femBC", 4)):
    a = np.array([x[idx] for x in bl if x[0]]); b = np.array([x[idx] for x in bl if not x[0]])
    print("  %-11s blank: %s numeric: %s" % (name, (round(a.min(),3), round(float(np.median(a)),3), round(a.max(),3)) if len(a) else None, (round(b.min(),3), round(float(np.median(b)),3), round(b.max(),3)) if len(b) else None))
