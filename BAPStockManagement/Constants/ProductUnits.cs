namespace BAPStockManagement.Constants;

public static class ProductUnits
{
    public const string Piece = "ชิ้น";
    public const string Case = "กระสอบ/ลัง/เส้น";
    public const string Bike = "คัน";

    public static bool IsCaseUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return false;
        }

        return unit.Contains("ลัง", StringComparison.Ordinal)
            || unit.Contains("กระสอบ", StringComparison.Ordinal)
            || unit.Contains("เส้น", StringComparison.Ordinal);
    }

    public static string Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return Piece;
        }

        var trimmed = unit.Trim();
        if (trimmed.Equals("ชิ้น", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("ชิ้น/กล่อง", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(Piece, StringComparison.OrdinalIgnoreCase))
        {
            return Piece;
        }

        if (trimmed.Equals("ลัง", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("กระสอบ/ลัง", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(Case, StringComparison.OrdinalIgnoreCase) ||
            IsCaseUnit(trimmed))
        {
            return Case;
        }

        return trimmed;
    }
}
