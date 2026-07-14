namespace BAPStockManagement.Constants;

public static class StockThresholds
{
    public const int LowStockLimit = 10;

    public static bool IsOutOfStock(int qtyPieces, int qtyCases)
        => qtyPieces == 0 && qtyCases == 0;

    /// <summary>
    /// ใกล้หมด: มีของอยู่ และยอดหลัก ≤ limit
    /// (มีชิ้นใช้ชิ้น, ไม่มีชิ้นแต่มีลังใช้ลัง)
    /// </summary>
    public static bool IsLowStock(int qtyPieces, int qtyCases)
    {
        if (qtyPieces > 0)
        {
            return qtyPieces <= LowStockLimit;
        }

        return qtyCases > 0 && qtyCases <= LowStockLimit;
    }
}
