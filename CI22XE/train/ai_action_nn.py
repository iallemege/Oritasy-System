#!/usr/bin/env python3
"""Imitation MLP for Oritasy AI action allocation.

Heads
  mode  6: Hunt Attack BreakOff Evade Cruise Acm
  acm   9: None JTurn HighYoYo LowYoYo LagPursuit LeadTurn Scissors EnergyExtend ZoomClimb

Teacher = current C# rules (AiCombatAcmPickService + Tick order).
Layout matches AiCombatNnService.cs (In=14 Hidden=24).

  python ai_action_nn.py train
  python ai_action_nn.py export-cs
"""
from __future__ import print_function

import math
import os
import random
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, "..", "src"))
NN_DIR = os.path.normpath(os.path.join(HERE, "..", "nn"))
WEIGHTS_PATH = os.path.join(NN_DIR, "ai_actions.weights")
CS_PATH = os.path.join(SRC, "AiCombatNnWeights.cs")
PLUGIN_OUT = r"D:\Steam\steamapps\common\Nuclear Option\BepInEx\plugins\OritasyNn\ai_actions.weights"

IN = 14
HIDDEN = 24
N_MODE = 6
N_ACM = 9
VERSION = 1
MAGIC = b"OANN"

MODE_HUNT = 0
MODE_ATTACK = 1
MODE_BREAK = 2
MODE_EVADE = 3
MODE_CRUISE = 4
MODE_ACM = 5

ACM_NONE = 0
ACM_JTURN = 1
ACM_HIGH_YOYO = 2
ACM_LOW_YOYO = 3
ACM_LAG = 4
ACM_LEAD = 5
ACM_SCISSORS = 6
ACM_EXTEND = 7
ACM_ZOOM = 8


def clamp(v, lo, hi):
    if v < lo:
        return lo
    if v > hi:
        return hi
    return v


def fill(dist, angle, aspect, my_spd, tgt_spd, corner, ralt, dalt,
         skill, acm_chance, can_shoot, should_break, threat, break_dist):
    c = max(float(corner), 1.0)
    x = [0.0] * IN
    x[0] = clamp(dist / 20000.0, 0.0, 1.0)
    x[1] = clamp(angle / 180.0, 0.0, 1.0)
    x[2] = clamp(aspect / 180.0, 0.0, 1.0)
    x[3] = clamp(my_spd / c, 0.0, 2.0) / 2.0
    x[4] = clamp(tgt_spd / c, 0.0, 2.0) / 2.0
    x[5] = clamp(ralt / 8000.0, 0.0, 1.0)
    x[6] = clamp((dalt + 4000.0) / 8000.0, 0.0, 1.0)
    x[7] = clamp(skill, 0.0, 1.0)
    x[8] = clamp(acm_chance, 0.0, 1.0)
    x[9] = 1.0 if can_shoot else 0.0
    x[10] = 1.0 if should_break else 0.0
    x[11] = 1.0 if threat else 0.0
    x[12] = clamp(my_spd / max(tgt_spd, 1.0), 0.0, 2.0) / 2.0
    x[13] = clamp(dist / max(break_dist, 1.0), 0.0, 4.0) / 4.0
    return x


def pick_acm(dist, angle, aspect, my_spd, tgt_spd, corner, ralt, break_dist, skill, dalt):
    """Clone of AiCombatAcmPickService.Pick (first match)."""
    if dist < break_dist * 1.35 and angle < 55.0 and my_spd > corner * 0.85:
        return ACM_JTURN
    close_fight = dist < 2800.0
    if (not close_fight) and abs(dalt) > 1200.0:
        if dalt > 1200.0 and ralt < 4500.0:
            return ACM_ZOOM
        if dalt < -1200.0 and ralt > 260.0:
            return ACM_LOW_YOYO
        return ACM_HIGH_YOYO
    if dist < 2800.0 and dist > 400.0 and my_spd > tgt_spd * 1.08 and angle > 25.0 and ralt > 350.0:
        return ACM_HIGH_YOYO
    if dist < 2200.0 and my_spd < corner * 0.92 and angle > 30.0 and ralt > 280.0:
        return ACM_LOW_YOYO
    if aspect > 110.0 and angle > 18.0 and dist < 3500.0 and dist > 300.0:
        return ACM_LAG
    if aspect < 50.0 and angle < 50.0 and dist > 800.0 and dist < 6000.0:
        return ACM_LEAD
    if dist < 1600.0 and angle > 40.0 and my_spd < corner * 1.15 and skill > 0.4:
        return ACM_SCISSORS
    if dist < break_dist * 1.1 and my_spd > corner and ralt > 200.0:
        return ACM_EXTEND
    if my_spd > corner * 1.25 and ralt < 1200.0 and dist > 4000.0 and angle < 25.0 and skill > 0.5:
        return ACM_ZOOM
    return ACM_NONE


def can_shoot_fn(dist, angle, gun_max, gun_cone, missile_max, missile_min, missile_cone, use_gun):
    if use_gun:
        return dist < gun_max and angle < gun_cone
    return dist < missile_max and dist > missile_min and angle < missile_cone


def should_break_fn(dist, angle, break_dist):
    return dist < break_dist and angle < 50.0


def rule_mode(threat, pick, acm_chance, roll_acm, should_break, prefer_acm_roll, can_shoot, my_spd, corner):
    """Tick order: ACM pick first, then break, then attack/hunt. Evade if threat."""
    if threat:
        return MODE_EVADE
    if pick != ACM_NONE and roll_acm <= acm_chance:
        return MODE_ACM
    if should_break:
        if acm_chance > 0.5 and prefer_acm_roll < acm_chance:
            return MODE_ACM
        return MODE_BREAK
    if can_shoot:
        return MODE_ATTACK
    return MODE_HUNT


def rule_acm_committed(pick, mode, should_break, my_spd, corner):
    if mode != MODE_ACM:
        return ACM_NONE if mode != MODE_ACM else pick
    if pick != ACM_NONE:
        return pick
    if should_break:
        if my_spd > corner * 0.9:
            return ACM_JTURN
        return ACM_EXTEND
    return ACM_NONE


TIERS = [
    dict(skill=0.32, acm=0.32, brk=1100.0, gun_max=850.0, gun_cone=14.0,
         msl_max=7500.0, msl_min=1400.0, msl_cone=20.0),
    dict(skill=0.50, acm=0.58, brk=800.0, gun_max=1200.0, gun_cone=9.0,
         msl_max=12000.0, msl_min=900.0, msl_cone=14.0),
    dict(skill=0.68, acm=0.82, brk=580.0, gun_max=1650.0, gun_cone=7.0,
         msl_max=17000.0, msl_min=650.0, msl_cone=11.0),
    dict(skill=0.85, acm=0.93, brk=420.0, gun_max=2100.0, gun_cone=5.5,
         msl_max=22000.0, msl_min=450.0, msl_cone=9.0),
    dict(skill=0.98, acm=0.98, brk=320.0, gun_max=2600.0, gun_cone=4.0,
         msl_max=30000.0, msl_min=280.0, msl_cone=7.0),
]


def _pack_row(rng, t, dist, angle, aspect, corner, my_spd, tgt_spd, ralt, dalt, threat):
    angle = clamp(angle, 0.0, 180.0)
    aspect = clamp(aspect, 0.0, 180.0)
    use_gun = rng.random() < 0.45
    can_shoot = can_shoot_fn(
        dist, angle, t["gun_max"], t["gun_cone"],
        t["msl_max"], t["msl_min"], t["msl_cone"], use_gun)
    should_break = should_break_fn(dist, angle, t["brk"])
    pick = pick_acm(
        dist, angle, aspect, my_spd, tgt_spd, corner, ralt, t["brk"], t["skill"], dalt)
    roll_acm = rng.random()
    prefer = rng.random()
    mode = rule_mode(
        threat, pick, t["acm"], roll_acm, should_break, prefer, can_shoot, my_spd, corner)
    acm = rule_acm_committed(pick, mode, should_break, my_spd, corner)
    x = fill(
        dist, angle, aspect, my_spd, tgt_spd, corner, ralt, dalt,
        t["skill"], t["acm"], can_shoot, should_break, threat, t["brk"])
    label_acm = acm if mode == MODE_ACM else pick
    return x, mode, label_acm


def sample_row(rng, want_acm=None):
    t = dict(TIERS[rng.randrange(len(TIERS))])
    threat = rng.random() < 0.05
    corner = rng.uniform(110.0, 175.0)
    tgt_spd = clamp(rng.gauss(corner, 50.0), 70.0, 480.0)

    def try_geom():
        if want_acm == ACM_JTURN:
            dist = t["brk"] * rng.uniform(0.45, 1.30)
            angle = rng.uniform(4.0, 52.0)
            aspect = rng.uniform(10.0, 170.0)
            my_spd = corner * rng.uniform(0.88, 1.45)
            ralt = rng.uniform(180.0, 5000.0)
            dalt = rng.uniform(-600.0, 600.0)
        elif want_acm == ACM_ZOOM:
            if rng.random() < 0.55:
                dist = rng.uniform(3000.0, 10000.0)
                dalt = rng.uniform(1300.0, 2800.0)
                ralt = rng.uniform(200.0, 4200.0)
                angle = rng.uniform(5.0, 80.0)
                aspect = rng.uniform(20.0, 160.0)
                my_spd = corner * rng.uniform(0.9, 1.4)
            else:
                dist = rng.uniform(4200.0, 12000.0)
                dalt = rng.uniform(-400.0, 400.0)
                ralt = rng.uniform(80.0, 1100.0)
                angle = rng.uniform(2.0, 22.0)
                aspect = rng.uniform(20.0, 160.0)
                my_spd = corner * rng.uniform(1.28, 1.6)
                t["skill"] = max(t["skill"], 0.55)
        elif want_acm == ACM_LOW_YOYO:
            if rng.random() < 0.5:
                dist = rng.uniform(3000.0, 9000.0)
                dalt = -rng.uniform(1300.0, 2800.0)
                ralt = rng.uniform(300.0, 4000.0)
                angle = rng.uniform(10.0, 90.0)
                aspect = rng.uniform(20.0, 160.0)
                my_spd = corner * rng.uniform(0.8, 1.3)
            else:
                dist = rng.uniform(400.0, 2100.0)
                dalt = rng.uniform(-400.0, 400.0)
                ralt = rng.uniform(300.0, 3000.0)
                angle = rng.uniform(32.0, 90.0)
                aspect = rng.uniform(40.0, 100.0)
                my_spd = corner * rng.uniform(0.55, 0.88)
        elif want_acm == ACM_HIGH_YOYO:
            dist = rng.uniform(500.0, 2700.0)
            dalt = rng.uniform(-200.0, 200.0)
            ralt = rng.uniform(400.0, 4000.0)
            angle = rng.uniform(28.0, 80.0)
            aspect = rng.uniform(40.0, 100.0)
            my_spd = tgt_spd * rng.uniform(1.12, 1.45)
        elif want_acm == ACM_LAG:
            dist = rng.uniform(350.0, 3300.0)
            angle = rng.uniform(22.0, 80.0)
            aspect = rng.uniform(115.0, 175.0)
            my_spd = corner * rng.uniform(0.85, 1.2)
            ralt = rng.uniform(200.0, 4000.0)
            dalt = rng.uniform(-500.0, 500.0)
        elif want_acm == ACM_LEAD:
            dist = rng.uniform(900.0, 5500.0)
            angle = rng.uniform(5.0, 48.0)
            aspect = rng.uniform(5.0, 48.0)
            my_spd = corner * rng.uniform(0.9, 1.25)
            ralt = rng.uniform(300.0, 4000.0)
            dalt = rng.uniform(-400.0, 400.0)
        elif want_acm == ACM_SCISSORS:
            dist = rng.uniform(400.0, 1500.0)
            angle = rng.uniform(42.0, 90.0)
            aspect = rng.uniform(40.0, 100.0)
            my_spd = corner * rng.uniform(0.7, 1.1)
            ralt = rng.uniform(250.0, 2500.0)
            dalt = rng.uniform(-300.0, 300.0)
            t["skill"] = max(t["skill"], 0.45)
        elif want_acm == ACM_EXTEND:
            dist = t["brk"] * rng.uniform(0.5, 1.05)
            angle = rng.uniform(55.0, 100.0)
            aspect = rng.uniform(40.0, 100.0)
            my_spd = corner * rng.uniform(1.05, 1.5)
            ralt = rng.uniform(250.0, 3000.0)
            dalt = rng.uniform(-200.0, 200.0)
        elif want_acm == ACM_NONE:
            dist = rng.uniform(6000.0, 18000.0)
            angle = rng.uniform(20.0, 90.0)
            aspect = rng.uniform(40.0, 120.0)
            my_spd = corner * rng.uniform(0.9, 1.15)
            ralt = rng.uniform(800.0, 4000.0)
            dalt = rng.uniform(-400.0, 400.0)
        else:
            dist = math.exp(rng.uniform(math.log(80.0), math.log(24000.0)))
            angle = clamp(abs(rng.gauss(25.0, 40.0)), 0.0, 180.0)
            aspect = clamp(abs(rng.gauss(90.0, 55.0)), 0.0, 180.0)
            my_spd = clamp(rng.gauss(corner * 1.05, 45.0), 70.0, 480.0)
            ralt = math.exp(rng.uniform(math.log(40.0), math.log(7000.0)))
            dalt = rng.gauss(0.0, 900.0)
        return _pack_row(
            rng, t, dist, angle, aspect, corner, my_spd, tgt_spd, ralt, dalt, threat)

    if want_acm is None:
        return try_geom()
    for _ in range(80):
        x, m, a = try_geom()
        if a == want_acm:
            return x, m, a
    return try_geom()


def make_dataset(n, seed=0):
    rng = random.Random(seed)
    per = max(16, n // N_ACM)
    xs, ym, ya = [], [], []
    for kind in range(N_ACM):
        got = 0
        spins = 0
        while got < per and spins < per * 90:
            spins += 1
            x, m, a = sample_row(rng, want_acm=kind)
            if a != kind:
                continue
            xs.append(x)
            ym.append(m)
            ya.append(a)
            got += 1
    extra = max(0, n - len(xs))
    for _ in range(extra):
        x, m, a = sample_row(rng, want_acm=None)
        xs.append(x)
        ym.append(m)
        ya.append(a)
    order = list(range(len(xs)))
    rng.shuffle(order)
    xs = [xs[i] for i in order]
    ym = [ym[i] for i in order]
    ya = [ya[i] for i in order]
    return xs, ym, ya


def make_dataset(n, seed=0):
    rng = random.Random(seed)
    xs, ym, ya = [], [], []
    for _ in range(n):
        x, m, a = sample_row(rng)
        xs.append(x)
        ym.append(m)
        ya.append(a)
    return xs, ym, ya


def count(n):
    return HIDDEN * IN + HIDDEN + N_MODE * HIDDEN + N_MODE + N_ACM * HIDDEN + N_ACM


def pack_blobs(W1, b1, Wm, bm, Wa, ba):
    blobs = []
    blobs.extend(W1.reshape(-1).tolist())
    blobs.extend(b1.tolist())
    blobs.extend(Wm.reshape(-1).tolist())
    blobs.extend(bm.tolist())
    blobs.extend(Wa.reshape(-1).tolist())
    blobs.extend(ba.tolist())
    if len(blobs) != count(0):
        raise RuntimeError("weight count %s != %s" % (len(blobs), count(0)))
    return blobs


def write_weights(path, blobs):
    folder = os.path.dirname(path)
    if folder and not os.path.isdir(folder):
        os.makedirs(folder)
    raw = MAGIC + struct.pack("<6i", VERSION, IN, HIDDEN, N_MODE, N_ACM, len(blobs))
    raw += struct.pack("<%sf" % len(blobs), *blobs)
    with open(path, "wb") as f:
        f.write(raw)
    print("wrote %s (%s floats, %s bytes)" % (path, len(blobs), len(raw)))


def emit_cs(blobs):
    lines = []
    lines.append("// <auto-generated> from train/ai_action_nn.py. Do not hand-edit.")
    lines.append("namespace Oritasy")
    lines.append("{")
    lines.append("    internal static class AiCombatNnWeights")
    lines.append("    {")
    lines.append("        internal const int Version = %d;" % VERSION)
    lines.append("        internal const int In = %d;" % IN)
    lines.append("        internal const int Hidden = %d;" % HIDDEN)
    lines.append("        internal const int NMode = %d;" % N_MODE)
    lines.append("        internal const int NAcm = %d;" % N_ACM)
    lines.append("        internal static readonly float[] Data = new float[]")
    lines.append("        {")
    row = []
    for i, v in enumerate(blobs):
        row.append("%.8ff" % float(v))
        if len(row) == 8 or i == len(blobs) - 1:
            lines.append("            " + ", ".join(row) + ("," if i != len(blobs) - 1 else ""))
            row = []
    lines.append("        };")
    lines.append("    }")
    lines.append("}")
    lines.append("")
    text = "\n".join(lines)
    with open(CS_PATH, "w") as f:
        f.write(text)
    print("wrote %s" % CS_PATH)


def relu(v):
    return v if v > 0.0 else 0.0


def softmax_ce_grad(logits, y):
    m = max(logits)
    ex = [math.exp(v - m) for v in logits]
    s = sum(ex)
    p = [e / s for e in ex]
    loss = -math.log(max(p[y], 1e-8))
    g = list(p)
    g[y] -= 1.0
    return loss, g, p


def train_numpy(n_train=36000, n_val=4500, epochs=12, batch=128, lr=0.055, seed=1):
    import numpy as np

    print("sampling train=%s val=%s" % (n_train, n_val))
    xt, ymt, yat = make_dataset(n_train, seed)
    xv, ymv, yav = make_dataset(n_val, seed + 17)
    X = np.asarray(xt, dtype=np.float64)
    Ym = np.asarray(ymt, dtype=np.int64)
    Ya = np.asarray(yat, dtype=np.int64)
    Xv = np.asarray(xv, dtype=np.float64)
    Ymv = np.asarray(ymv, dtype=np.int64)
    Yav = np.asarray(yav, dtype=np.int64)

    rng = np.random.RandomState(seed)
    scale1 = math.sqrt(2.0 / IN)
    scale2 = math.sqrt(2.0 / HIDDEN)
    W1 = rng.randn(HIDDEN, IN) * scale1
    b1 = np.zeros(HIDDEN)
    Wm = rng.randn(N_MODE, HIDDEN) * scale2
    bm = np.zeros(N_MODE)
    Wa = rng.randn(N_ACM, HIDDEN) * scale2
    ba = np.zeros(N_ACM)

    n = X.shape[0]
    idx = np.arange(n)

    def forward_batch(xb):
        h = xb.dot(W1.T) + b1
        h = np.maximum(h, 0.0)
        lm = h.dot(Wm.T) + bm
        la = h.dot(Wa.T) + ba
        return h, lm, la

    def acc(xb, ym, ya):
        _, lm, la = forward_batch(xb)
        pm = np.argmax(lm, axis=1)
        pa = np.argmax(la, axis=1)
        am = float(np.mean(pm == ym))
        aa = float(np.mean(pa == ya))
        nz = ya != 0
        aan = float(np.mean(pa[nz] == ya[nz])) if np.any(nz) else 0.0
        return am, aa, aan

    for ep in range(epochs):
        rng.shuffle(idx)
        loss_sum = 0.0
        steps = 0
        for start in range(0, n, batch):
            sl = idx[start:start + batch]
            xb = X[sl]
            ym = Ym[sl]
            ya = Ya[sl]
            h, lm, la = forward_batch(xb)
            # softmax CE
            lm = lm - lm.max(axis=1, keepdims=True)
            la = la - la.max(axis=1, keepdims=True)
            em = np.exp(lm)
            ea = np.exp(la)
            pm = em / em.sum(axis=1, keepdims=True)
            pa = ea / ea.sum(axis=1, keepdims=True)
            bsz = xb.shape[0]
            w_m = 1.0 / (np.bincount(Ym, minlength=N_MODE)[ym] + 8.0)
            w_a = 1.0 / (np.bincount(Ya, minlength=N_ACM)[ya] + 8.0)
            w_m = w_m / w_m.mean()
            w_a = w_a / w_a.mean()
            loss = -(np.log(pm[np.arange(bsz), ym] + 1e-8) * w_m).mean()
            loss += -(np.log(pa[np.arange(bsz), ya] + 1e-8) * w_a).mean()
            gm = pm.copy()
            ga = pa.copy()
            gm[np.arange(bsz), ym] -= 1.0
            ga[np.arange(bsz), ya] -= 1.0
            gm *= w_m[:, None]
            ga *= w_a[:, None]
            gm /= bsz
            ga /= bsz
            dWm = gm.T.dot(h)
            dbm = gm.sum(axis=0)
            dWa = ga.T.dot(h)
            dba = ga.sum(axis=0)
            dh = gm.dot(Wm) + ga.dot(Wa)
            dh[h <= 0.0] = 0.0
            dW1 = dh.T.dot(xb)
            db1 = dh.sum(axis=0)
            W1 -= lr * dW1
            b1 -= lr * db1
            Wm -= lr * dWm
            bm -= lr * dbm
            Wa -= lr * dWa
            ba -= lr * dba
            loss_sum += float(loss)
            steps += 1
        am, aa, aan = acc(Xv, Ymv, Yav)
        print("epoch %s  loss=%.4f  val mode=%.3f acm=%.3f acm_nz=%.3f  lr=%.4f" % (
            ep + 1, loss_sum / max(steps, 1), am, aa, aan, lr))
        lr *= 0.82

    am, aa, aan = acc(Xv, Ymv, Yav)
    print("final val  mode=%.3f  acm=%.3f acm_nz=%.3f" % (am, aa, aan))
    blobs = pack_blobs(W1.astype(np.float64), b1, Wm.astype(np.float64), bm, Wa.astype(np.float64), ba)
    return blobs, am, aa


def train_torch(n_train=48000, n_val=6000, epochs=8, batch=128, lr=0.03, seed=1):
    import numpy as np
    import torch
    import torch.nn as nn
    import torch.nn.functional as F

    print("sampling train=%s val=%s (torch)" % (n_train, n_val))
    xt, ymt, yat = make_dataset(n_train, seed)
    xv, ymv, yav = make_dataset(n_val, seed + 17)
    X = torch.tensor(xt, dtype=torch.float32)
    Ym = torch.tensor(ymt, dtype=torch.long)
    Ya = torch.tensor(yat, dtype=torch.long)
    Xv = torch.tensor(xv, dtype=torch.float32)
    Ymv = torch.tensor(ymv, dtype=torch.long)
    Yav = torch.tensor(yav, dtype=torch.long)

    class Net(nn.Module):
        def __init__(self):
            super(Net, self).__init__()
            self.fc1 = nn.Linear(IN, HIDDEN)
            self.mode = nn.Linear(HIDDEN, N_MODE)
            self.acm = nn.Linear(HIDDEN, N_ACM)

        def forward(self, x):
            h = F.relu(self.fc1(x))
            return self.mode(h), self.acm(h)

    torch.manual_seed(seed)
    net = Net()
    opt = torch.optim.Adam(net.parameters(), lr=lr)
    n = X.shape[0]
    idx = torch.randperm(n)
    for ep in range(epochs):
        idx = torch.randperm(n)
        loss_sum = 0.0
        steps = 0
        net.train()
        for start in range(0, n, batch):
            sl = idx[start:start + batch]
            opt.zero_grad()
            lm, la = net(X[sl])
            loss = F.cross_entropy(lm, Ym[sl]) + F.cross_entropy(la, Ya[sl])
            loss.backward()
            opt.step()
            loss_sum += float(loss.item())
            steps += 1
        net.eval()
        with torch.no_grad():
            lm, la = net(Xv)
            am = float((lm.argmax(1) == Ymv).float().mean())
            aa = float((la.argmax(1) == Yav).float().mean())
        print("epoch %s  loss=%.4f  val mode=%.3f acm=%.3f" % (
            ep + 1, loss_sum / max(steps, 1), am, aa))

    net.eval()
    W1 = net.fc1.weight.detach().cpu().numpy().astype(np.float64)
    b1 = net.fc1.bias.detach().cpu().numpy().astype(np.float64)
    Wm = net.mode.weight.detach().cpu().numpy().astype(np.float64)
    bm = net.mode.bias.detach().cpu().numpy().astype(np.float64)
    Wa = net.acm.weight.detach().cpu().numpy().astype(np.float64)
    ba = net.acm.bias.detach().cpu().numpy().astype(np.float64)
    blobs = pack_blobs(W1, b1, Wm, bm, Wa, ba)
    with torch.no_grad():
        lm, la = net(Xv)
        am = float((lm.argmax(1) == Ymv).float().mean())
        aa = float((la.argmax(1) == Yav).float().mean())
    print("final val  mode=%.3f  acm=%.3f" % (am, aa))
    return blobs, am, aa


def save_all(blobs):
    write_weights(WEIGHTS_PATH, blobs)
    emit_cs(blobs)
    try:
        plug_dir = os.path.dirname(PLUGIN_OUT)
        if os.path.isdir(os.path.dirname(plug_dir)):
            if not os.path.isdir(plug_dir):
                os.makedirs(plug_dir)
            write_weights(PLUGIN_OUT, blobs)
    except OSError as ex:
        print("plugin copy skipped:", ex)


def main():
    cmd = "train"
    if len(sys.argv) > 1:
        cmd = sys.argv[1]
    if cmd in ("train", "export-cs"):
        blobs = None
        am = aa = 0.0
        try:
            blobs, am, aa = train_torch()
        except ImportError:
            print("torch missing, numpy SGD")
            blobs, am, aa = train_numpy()
        if am < 0.62 or aa < 0.50:
            print("WARNING: imitation accuracy is low (mode=%.3f acm=%.3f)" % (am, aa))
        save_all(blobs)
        return
    raise SystemExit("usage: python ai_action_nn.py train")


if __name__ == "__main__":
    main()
