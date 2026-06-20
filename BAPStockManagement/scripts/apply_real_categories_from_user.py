import pyodbc

DB_CONN = (
    "Driver={ODBC Driver 17 for SQL Server};"
    r"Server=localhost\SQLEXPRESS03;"
    "Database=BAPStockDB;"
    "Trusted_Connection=yes;"
    "TrustServerCertificate=yes;"
)

CATEGORY_SKUS = {
    "กลุ่มจักรบยาน-1(LION)": """
BLB-A013,BLB-A025,BLB-A025,BLB-A029,BLB-A030,BLB-A031,BLB-A032,BLB-A033,BLB-A035,BLB-A035,BLB-A036,BLB-A036,BLB-A037,BLB-A038,BLB-A038,BLB-A039,BLB-A040,BLB-A040,BLB-A042,BLB-A043,BLB-A044,BLB-A044,BLB-A044,BLB-A045,BLB-A045,BLB-A047,BLB-A047,BLB-A049,BLB-A050,BLB-A050,BLB-A051,BLB-A052,BLB-A052,BLB-A053,BLB-A053,BLB-A048,BLB-A048,BLB-A054,BLB-A055,BLB-A055,BLB-A056,BLB-A057,BLB-A058,BLB-A058,BLB-A058,BLB-A059,BLB-A059,BLB-A059,BLB-A060,BLB-A060,BLB-A061
""",
    "กลุ่มจักรยาน-5-BL-V": """
BLB-FTC,BLB-FTC,BLB-X1,BLB-AL109,BLB-AL110,BLB-AL112,BLB-AL113,BLB-AL114
""",
    "กลุ่มรถหัดเดิน-1": """
BL-LNH-8014-S,BL-501,BL-502,BL-505,BL-507,BL-508,BL-509
""",
    "กลุ่มรถหัดเดินไฟฟ้า-1-BL-L": """
BL-506,BL-510,BL-511,BL-512,BL-513
""",
    "กลุ่มรถหัดเดิน-1-BL-L": """
BL-514,BL-515,BL-517,BL-518,BL-520,BL-521,BL-522,BL-523
""",
    "กลุ่มรถหัดเดิน-2BL-E": """
BL-E-009-B,BL-E-009-A,BLE-W18A
""",
    "ยางในจักรยาน-COLUN": """
BLC-12-175,BLC-14-175,BLC-16-175,BLC-20-175,BLC-24-175,BLC-26-175,BLC-26-138
""",
    "ยางในมอเตอร์ไซค์ BLUE": """
BL-BR-200-17,BL-BR-225-17,BL-BR-250-17,BL-BR-275-17,BL-BR-250-14,BL-BR-275-14,BL-BR-26-212
""",
    "ยางนอกจักรยานยนต์-COLUN": """
BLC-60/100-17,BLC-70/90-17,BLC-80/90-17,BLC-60/100-17,BLC-70/90-17,BLC-80/90-17,BLC-60/100-17,BLC-70/90-17,BLC-80/90-17,BLC-70/90-14,BLC-80/90-14,BLC-90/90-14
""",
    "ยางนอกจักรยาน-PKT": """
BLP-12-175,BLP-14-175,BLP-16-175,BLP-20-175,BLP-24-175,BLP-26-175
""",
    "อะไหล่รถไฟฟ้า": """
BL-L-001,BL-L002,BL-L003,BL-L004,BL-L005,BL-L006,BL-L007,BL-L011,BL-L012,BL-L013-บน,BL-L014-บน,BL-L015,BL-L016-บน,BL-L021-บน,BL-L022-บน,BL-L023-บน,BL-L024-บน,BL-L025-บน,BL-L026-บน,BL-L031,BL-L032-บน,BL-L033,BL-L034,BL-L035,BL-L036,BL-L037,BL-L039,BL-L038,BL-L040,BL-L041-บน,BL-L042,BL-L043,BL-L044,BL-L045-บน,BL-L046,BL-L047-บน,BL-L048,BL-L049,BL-L050-บน,BL-L051,BL-L008,BL-B011,BL-L052-บน,BL-L053,BL-L054,BL-L051-บน,BL-L055,BL-L056,BL-L057,BL-L058-บน,BL-L059,BL-L060,BL-L061-บน,BL-L062,BL-L063,BL-L064,BL-L065,BL-L066,BL-L067-บน,BL-L068,BL-L069-บน,BL-L070-บน,BL-L071,BL-L072-บน,BL-L073-บน,BL-L074,BL-L075-บน,BL-L076,BL-L077
""",
    "อะไหล่รถจักรยาน": """
BL-L110,BL-L111,BL-L112,BL-L113,BL-L114,BL-L115,BL-L116,BL-L117,BL-L118,BL-L119,BL-L120,BL-L121,BL-L122,BL-L123
""",
    "สระน้ำ": """
BLL-SP-200-2V01,BLL-SP-200-2V02,BLL-SP-262-2V01,BLL-SP-262-2V02,BLL-SP-305-3V01,BLL-SP-305-3V02,BLL-SP-120-4,BLL-SP-305-3,BLL-SP-305-4,BLL-JL-262-2,BLL-L57179,BLL-L17804,BLL-616,BLL-SP-200A-2,BLL-SP-262A-2,BLL-SP-120-2,BLL-SP-150-2,BLL-57180,BLL-JL-200-2,BLL-SP-180-3,BLL-JL-305-3V01,BLL-17491VO1
""",
}


def parse_skus(raw_text):
    return [token.strip().upper() for token in raw_text.replace("\n", ",").split(",") if token.strip()]


def main():
    conn = pyodbc.connect(DB_CONN)
    conn.autocommit = False
    cur = conn.cursor()

    target_categories = list(CATEGORY_SKUS.keys())
    category_ids = {}

    # Ensure only these categories are active
    for index, name in enumerate(target_categories, start=1):
        cur.execute("SELECT CategoryID FROM Categories WHERE CategoryName = ?", name)
        row = cur.fetchone()
        if row:
            category_id = int(row[0])
            cur.execute(
                "UPDATE Categories SET IsActive = 1, SortOrder = ?, VariantLabel = N'สี' WHERE CategoryID = ?",
                index,
                category_id,
            )
        else:
            cur.execute(
                """
                INSERT INTO Categories (CategoryName, VariantLabel, SortOrder, IsActive)
                VALUES (?, N'สี', ?, 1)
                """,
                name,
                index,
            )
            cur.execute("SELECT CategoryID FROM Categories WHERE CategoryName = ?", name)
            category_id = int(cur.fetchone()[0])

        category_ids[name] = category_id

    placeholders = ",".join(["?"] * len(target_categories))
    cur.execute(f"UPDATE Categories SET IsActive = 0 WHERE CategoryName NOT IN ({placeholders})", target_categories)

    # Build sku -> category map
    sku_to_category = {}
    collisions = []
    for category_name, sku_text in CATEGORY_SKUS.items():
        for sku in parse_skus(sku_text):
            if sku in sku_to_category and sku_to_category[sku] != category_name:
                collisions.append((sku, sku_to_category[sku], category_name))
            else:
                sku_to_category[sku] = category_name

    updated = 0
    unmatched = []

    for sku, category_name in sku_to_category.items():
        category_id = category_ids[category_name]
        cur.execute(
            """
            UPDATE Products
            SET CategoryID = ?, IsActive = 1, UpdatedAt = SYSDATETIME()
            WHERE UPPER(SKU) = ? AND CategoryID <> ?
            """,
            category_id,
            sku,
            category_id,
        )
        updated += int(cur.rowcount)

        cur.execute("SELECT COUNT(1) FROM Products WHERE UPPER(SKU) = ?", sku)
        if int(cur.fetchone()[0]) == 0:
            unmatched.append(sku)

    conn.commit()

    # Summary
    cur.execute(
        f"""
        SELECT CategoryName, COUNT(1)
        FROM Products p
        JOIN Categories c ON c.CategoryID = p.CategoryID
        WHERE c.CategoryName IN ({placeholders})
        GROUP BY CategoryName
        ORDER BY MIN(c.SortOrder), CategoryName
        """,
        target_categories,
    )
    rows = cur.fetchall()

    print(f"CATEGORY_SYNC_DONE | categories={len(target_categories)} | updated={updated} | unmatched_skus={len(unmatched)} | collisions={len(collisions)}")
    for name, count in rows:
        safe_name = str(name).encode("unicode_escape").decode("ascii")
        print(f"{safe_name}: {count}")

    if unmatched:
        print("UNMATCHED:")
        for sku in sorted(set(unmatched)):
            print(sku)

    if collisions:
        print("COLLISIONS:")
        for sku, old, new in collisions:
            print(f"{sku}: {old} -> {new}")

    conn.close()


if __name__ == "__main__":
    main()
