import pyodbc

DB_CONN = (
    "Driver={ODBC Driver 17 for SQL Server};"
    r"Server=localhost\SQLEXPRESS03;"
    "Database=BAPStockDB;"
    "Trusted_Connection=yes;"
    "TrustServerCertificate=yes;"
)


def main():
    conn = pyodbc.connect(DB_CONN)
    conn.autocommit = False
    cur = conn.cursor()

    cur.execute(
        """
        SELECT c.CategoryID, c.CategoryName, COUNT(p.ProductID) AS ProductCount
        FROM Categories c
        LEFT JOIN Products p ON p.CategoryID = c.CategoryID
        WHERE c.IsActive = 0
        GROUP BY c.CategoryID, c.CategoryName
        ORDER BY c.CategoryName
        """
    )
    rows = cur.fetchall()

    deletable_ids = [int(r[0]) for r in rows if int(r[2]) == 0]
    blocked = [(str(r[1]), int(r[2])) for r in rows if int(r[2]) > 0]

    deleted = 0
    for category_id in deletable_ids:
        cur.execute("DELETE FROM Categories WHERE CategoryID = ?", category_id)
        deleted += int(cur.rowcount)

    conn.commit()
    conn.close()

    print(f"INACTIVE_CATEGORY_CLEANUP | deleted={deleted} | blocked={len(blocked)}")
    for name, product_count in blocked:
        safe_name = name.encode("unicode_escape").decode("ascii")
        print(f"BLOCKED {safe_name} products={product_count}")


if __name__ == "__main__":
    main()
