#!/usr/bin/env python3
"""Builds the Derelict crew ship map from the stock NT-Dart shuttle by appending
the extra fixtures the game mode needs (spawn points, consoles, cargo pallets).

Usage: python3 Tools/derelict_make_ship.py   (run from the repository root)
"""
import re

SRC = "Resources/Maps/Shuttles/dart.yml"
DST = "Resources/Maps/_Derelict/crew_ship.yml"

# (prototype, [(x, y, rotation in radians or None)])
EXTRA = [
    ("SpawnPointLatejoin", [(-3.5, -1.5, None), (-1.5, -1.5, None), (0.5, -1.5, None), (2.5, -1.5, None)]),
    ("SpawnPointDerelictSalvager", [(-3.5, -1.5, None), (-1.5, -1.5, None), (0.5, -1.5, None), (2.5, -1.5, None)]),
    ("SpawnPointObserver", [(-0.5, -1.5, None)]),
    ("DerelictNavConsole", [(-2.5, 6.5, None)]),
    ("ComputerCargoOrders", [(1.5, 6.5, None)]),
    # In the main corridor next to the selling pallets, clear of the bridge seats.
    ("ComputerPalletConsole", [(-5.5, -0.5, None)]),
    ("CargoPalletBuy", [(-4.5, -0.5, None), (-3.5, -0.5, None)]),
    ("CargoPalletSell", [(-4.5, -2.5, None), (-3.5, -2.5, None), (-2.5, -2.5, None)]),
]

text = open(SRC, encoding="utf-8").read()
uid = max(int(m) for m in re.findall(r"^  - uid: (\d+)$", text, re.M)) + 1000
added = 0
text = text.rstrip("\n")
if text.endswith("..."):
    text = text[:-3].rstrip("\n")
out = [text]
for proto, spots in EXTRA:
    out.append(f"- proto: {proto}")
    out.append("  entities:")
    for x, y, rot in spots:
        out.append(f"  - uid: {uid}")
        out.append("    components:")
        out.append("    - type: Transform")
        if rot is not None:
            out.append(f"      rot: {rot} rad")
        out.append(f"      pos: {x},{y}")
        out.append("      parent: 1")
        uid += 1
        added += 1
result = "\n".join(out) + "\n...\n"
count = int(re.search(r"entityCount: (\d+)", result).group(1))
result = result.replace(f"entityCount: {count}", f"entityCount: {count + added}", 1)
result = result.replace("name: NT-Dart", "name: ST-Magpie", 1)
# The grid has to be claimed by the "Tug" station of the DerelictCrewShip game map.
assert "    - type: Broadphase\n" in result
result = result.replace("    - type: Broadphase\n", "    - type: BecomesStation\n      id: Tug\n    - type: Broadphase\n", 1)
open(DST, "w", encoding="utf-8").write(result)
print(f"wrote {DST} with {added} extra entities")
