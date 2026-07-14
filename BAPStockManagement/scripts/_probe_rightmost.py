# -*- coding: utf-8 -*-
from pathlib import Path
import openpyxl

path = Path(r"C:\Users\Admin\Downloads\สต็อกจักรยาน 0769.xlsx")
wb = openpyxl.load_workbook(path, data_only=True)
ws = wb[wb.sheetnames[0]]

row = None
for r in range(1, ws.max_row + 1):
    sku = ws.cell(r, 2).value
    name = ws.cell(r, 3).value
    if sku and "BLB-A025" in str(sku) and name and str(name).startswith("16"):
        row = r
        break

lines = [f"mtime={path.stat().st_mtime} size={path.stat().st_size} max_col={ws.max_column} row={row}"]

# Dump last 40 columns row1/row2/value for this product
start_c = max(1, ws.max_column - 40)
lines.append(f"--- columns {start_c}..{ws.max_column} ---")
for c in range(start_c, ws.max_column + 1):
    t = ws.cell(1, c).value
    h = ws.cell(2, c).value
    v = ws.cell(row, c).value
    lines.append(f"c{c}: r1={t!r} r2={h!r} val={v!r}")

# All stock starts with แดง
starts = []
for c in range(1, ws.max_column + 1):
    t = str(ws.cell(1, c).value or "").strip()
    h = str(ws.cell(2, c).value or "").strip()
    if "สต็อก" in t and "แดง" in h:
        starts.append(c)
lines.append(f"stock_starts={starts}")

# Last 3 stock blocks detail
for start in starts[-3:]:
    lines.append(f"=== block start {start} ===")
    for off in range(0, 16):
        c = start + off
        if c > ws.max_column:
            break
        h = str(ws.cell(2, c).value or "").strip()
        t = str(ws.cell(1, c).value or "").strip()
        v = ws.cell(row, c).value
        lines.append(f"  c{c} [{t}|{h}]={v}")

# Any cell = 375 on this row
hits = []
for c in range(1, ws.max_column + 1):
    v = ws.cell(row, c).value
    if v == 375 or v == 365:
        hits.append((c, str(ws.cell(1,c).value), str(ws.cell(2,c).value), v))
lines.append("hits 375/365: " + str(hits))

out = Path(r"C:\Users\Admin\Documents\GitHub\BAPSM\BAPStockManagement\scripts\_excel_rightmost.txt")
out.write_text("\n".join(lines), encoding="utf-8")
print("ok", len(lines))
