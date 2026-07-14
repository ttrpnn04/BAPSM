# -*- coding: utf-8 -*-
from pathlib import Path
import openpyxl

paths = [
    Path(r"C:\Users\Admin\Downloads\สต็อกจักรยาน 0769.xlsx"),
    *Path(r"C:\Users\Admin\OneDrive\Desktop").glob("*0769*.xlsx"),
]
paths = [p for p in paths if p.exists() and not p.name.startswith("~$")]

lines = []
for path in paths:
    wb = openpyxl.load_workbook(path, data_only=False)  # need styles/dims for hidden
    ws = wb[wb.sheetnames[0]]
    lines.append(f"==== {path} size={path.stat().st_size} max_col={ws.max_column} ====")

    bills = []
    stocks = []
    hidden_cols = []
    for c in range(1, ws.max_column + 1):
        t = str(ws.cell(1, c).value or "").strip()
        h = str(ws.cell(2, c).value or "").strip()
        dim = ws.column_dimensions.get(openpyxl.utils.get_column_letter(c))
        hidden = bool(dim and dim.hidden)
        if hidden:
            hidden_cols.append(c)
        if t.startswith("บิล"):
            if not bills or bills[-1][1] != t:
                bills.append((c, t, hidden))
        if "สต็อก" in t and "แดง" in h:
            stocks.append((c, hidden))

    lines.append(f"bill_sections={bills}")
    lines.append(f"stock_starts={stocks}")
    lines.append(f"hidden_col_count={len(hidden_cols)} hidden_sample={hidden_cols[:20]} ... last_hidden={[c for c in hidden_cols if c>=400]}")

    # specifically 420-467 hidden?
    for c in range(420, min(ws.max_column, 467) + 1):
        dim = ws.column_dimensions.get(openpyxl.utils.get_column_letter(c))
        hidden = bool(dim and dim.hidden)
        t = str(ws.cell(1, c).value or "").strip()
        h = str(ws.cell(2, c).value or "").strip()
        if t or h or hidden:
            if c >= 430 or t or hidden:
                if t or (h == "แดง") or hidden or c in (435, 436, 451, 452, 467):
                    lines.append(f"  c{c} hidden={hidden} r1={t!r} r2={h!r}")

    lines.append("")

out = Path(r"C:\Users\Admin\Documents\GitHub\BAPSM\BAPStockManagement\scripts\_excel_bills.txt")
out.write_text("\n".join(lines), encoding="utf-8")
print("ok")
