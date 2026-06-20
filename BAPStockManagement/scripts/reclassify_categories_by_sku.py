import pyodbc

DB_CONN = (
    "Driver={ODBC Driver 17 for SQL Server};"
    r"Server=localhost\SQLEXPRESS03;"
    "Database=BAPStockDB;"
    "Trusted_Connection=yes;"
    "TrustServerCertificate=yes;"
)

CATEGORY_NAME_BY_KEY = {
    "BL-BR": "BL-BR - ยางในมอเตอร์ไซต์",
    "BLC": "BLC - ยางในจักรยาน",
    "BLB": "BLB - จักรยาน",
    "BL": "BL - รถเด็กหัดเดิน",
}


def get_or_create_category(cur, name):
    cur.execute("SELECT CategoryID FROM Categories WHERE CategoryName = ?", name)
    row = cur.fetchone()
    if row:
        return int(row[0])

    cur.execute(
        """
        INSERT INTO Categories (CategoryName, VariantLabel, SortOrder, IsActive)
        VALUES (?, N'สี', 999, 1)
        """,
        name,
    )
    cur.execute("SELECT CategoryID FROM Categories WHERE CategoryName = ?", name)
    return int(cur.fetchone()[0])


def main():
    conn = pyodbc.connect(DB_CONN)
    conn.autocommit = False
    cur = conn.cursor()

    cat_ids = {key: get_or_create_category(cur, name) for key, name in CATEGORY_NAME_BY_KEY.items()}

    # Important: BL must not override BLB/BLC/BL-BR.
    # We classify in one pass with CASE to avoid overlapping-prefix overwrite.
    cur.execute(
        """
        UPDATE p
        SET p.CategoryID =
            CASE
                WHEN UPPER(p.SKU) LIKE 'BL-BR%' THEN ?
                WHEN UPPER(p.SKU) LIKE 'BLC%' THEN ?
                WHEN UPPER(p.SKU) LIKE 'BLB%' THEN ?
                WHEN UPPER(p.SKU) LIKE 'BL%' THEN ?
                ELSE p.CategoryID
            END,
            p.UpdatedAt = SYSDATETIME()
        FROM Products p
        WHERE UPPER(p.SKU) LIKE 'BL%'
           OR UPPER(p.SKU) LIKE 'BLC%'
           OR UPPER(p.SKU) LIKE 'BL-BR%'
        """,
        cat_ids["BL-BR"],
        cat_ids["BLC"],
        cat_ids["BLB"],
        cat_ids["BL"],
    )
    updated = int(cur.rowcount)

    conn.commit()
    conn.close()
    print(f"RECLASS_OK | updated={updated}")


if __name__ == "__main__":
    main()
