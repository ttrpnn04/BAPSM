# -*- coding: utf-8 -*-
import openpyxl
from pathlib import Path

files = [p for p in list(Path(r"C:\Users\Admin\Downloads").glob("*0769*.xlsx"))
              + list(Path(r"C:\Users\Admin\OneDrive\Desktop").glob("*0769*.xlsx"))
         if not p.name.startswith("~$")]

out = Path(r"C:\Users\Admin\Documents\GitHub\BAPSM\BAPStockManagement\scripts\_excel_probe.txt")
lines = []

for path in files:
    lines.append(f"==== {path.name} size={path.stat().st_size} mtime={path.stat().st_mtime} ====")
    wb = openpyxl.load_workbook(path, data_only=True)
    ws = wb[wb.sheetnames[0]]
    lines.append(f"sheet={ws.title} max_col={ws.max_column} max_row={ws.max_row}")

    # Row 1-2 headers for all columns
    stock_starts = []
    for c in range(1, min(ws.max_column, 200) + 1):
        t = ws.cell(1, c).value
        h = ws.cell(2, c).value
        t_s = str(t).strip() if t is not None else ""
        h_s = str(h).strip() if h is not None else ""
        if t_s and ("สต็อก" in t_s or "บิล" in t_s or "คงเหลือ" in t_s):
            lines.append(f"  col {c}: row1={t_s!r} row2={h_s!r}")
            if "สต็อก" in t_s:
                stock_starts.append(c)

    # Find BLB-A025 16" row
    target_row = None
    for r in range(1, ws.max_row + 1):
        sku = ws.cell(r, 2).value
        name = ws.cell(r, 3).value
        if sku and "BLB-A025" in str(sku) and name and "16" in str(name):
            target_row = r
            lines.append(f"FOUND row={r} sku={sku!r} name={name!r}")
            break

    if target_row:
        # dump non-empty cells in that row
        vals = []
        for c in range(1, min(ws.max_column, 200) + 1):
            v = ws.cell(target_row, c).value
            h = ws.cell(2, c).value
            t = ws.cell(1, c).value
            if v is None or v == "" or v == "-":
                continue
            vals.append((c, str(t)[:20] if t else "", str(h)[:20] if h else "", v))
        lines.append("NON-EMPTY CELLS:")
        for c, t, h, v in vals:
            lines.append(f"  c{c} [{t}|{h}] = {v}")

    # Simulate FindQuantityBlocks for สต็อก (naive old vs new)
    lines.append(f"stock_title_cols={stock_starts}")
    lines.append("")

out.write_text("\n".join(lines), encoding="utf-8")
print(f"wrote {out} lines={len(lines)}")
