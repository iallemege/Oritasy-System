# -*- coding: utf-8 -*-
import pathlib
tsv = pathlib.Path(__file__).resolve().parent.parent / "assets" / "OritasyZh" / "game_zh.tsv"
text = tsv.read_text(encoding="utf-8")
entries = [l for l in text.splitlines() if l and not l.startswith("#") and "\t" in l]
print("tsv", len(entries))
print("sample0", entries[0])
print("fs12", [e for e in entries if e.startswith("FS-12")][:2])
cs = (pathlib.Path(__file__).resolve().parent.parent / "src" / "GameZhLocalizer.cs").read_text(encoding="utf-8")
print("seed_Add_calls", cs.count('Add("'))
# desc rows: long EN keys
descs = [e for e in entries if len(e.split("\t", 1)[0]) > 80]
print("long_en_keys_likely_descs", len(descs))
names = [e for e in entries if len(e.split("\t", 1)[0]) <= 80]
print("short_keys", len(names))
