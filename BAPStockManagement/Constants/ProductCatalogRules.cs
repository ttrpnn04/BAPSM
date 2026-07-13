namespace BAPStockManagement.Constants;

public static class ProductCatalogRules
{
    // เฉพาะรายการที่นับกระสอบ/ลัง/เส้น ที่มาตรฐาน (นอกจากยางนอก/ยางใน)
    private static readonly string[] LooseCaseProductNames =
    [
        "Rollerblave(1กx6คัน)",
        "ขาไถ่ไดโนเสาร์หมุนได้ 360 องศา(1กล่องx6คัน)",
        "เซิร์ฟสเก็ต-คละสี(1กx1ชิ้น)",
        "รถสามล้อเด็กคละสี(1กx6ชิ้น)"
    ];

    public static bool UsesPackedCaseCategory(string? categoryName)
    {
        var name = Normalize(categoryName);
        return name.Equals("ยางในจักรยาน-COLUN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ยางในมอเตอร์ไซค์ BLUE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool UsesLooseCaseCategory(string? categoryName)
    {
        var name = Normalize(categoryName);
        return name.StartsWith("ยางนอก", StringComparison.OrdinalIgnoreCase);
    }

    public static bool UsesLooseCaseProduct(string? productName)
    {
        var name = Normalize(productName);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.Contains("ยางนอก", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return LooseCaseProductNames.Any(n =>
            name.Equals(n, StringComparison.OrdinalIgnoreCase));
    }

    public static bool UsesLooseCaseUnit(string? categoryName, string? productName) =>
        UsesLooseCaseCategory(categoryName) || UsesLooseCaseProduct(productName);

    public static bool UsesCaseQuantity(string? categoryName, string? productName = null) =>
        UsesPackedCaseCategory(categoryName) || UsesLooseCaseUnit(categoryName, productName);

    public static bool UsesStandardVariant(string? categoryName, string? productName = null)
    {
        var category = Normalize(categoryName);
        return UsesCaseQuantity(category, productName) ||
               category.StartsWith("อะไหล่", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
