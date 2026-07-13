namespace BAPStockManagement.Constants;

public static class ProductVariantDefaults
{
    public const string StandardVariantName = "มาตรฐาน";

    // ชื่อสีต้องตรงกับหัวตารางใน Excel (ไม่มีเขียวอ่อน / ชมพูอ่อน)
    public static readonly string[] ColorNames =
    [
        "แดง",
        "น้ำเงิน/ชม",
        "ฟ้า",
        "ส้ม",
        "เขียว",
        "ชมเข้ม/ชม/ชม",
        "เหลือง/ทอง/ครีม",
        "ม่วง",
        "วัว/ขาว /แดง",
        "ดำ/ธงฟ้า",
        "ตาล",
        "เทา"
    ];

    // ชื่อเก่า/พิมพ์ผิด / สีที่ยกเลิก → ชื่อตาม Excel
    public static readonly IReadOnlyDictionary<string, string> VariantAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["เขียวออ่อน"] = "เขียว",
            ["เขียวอ่อน"] = "เขียว",
            ["ชมพูอ่อน"] = "ชมเข้ม/ชม/ชม",
            ["เหลืองฝทอง/ครีม"] = "เหลือง/ทอง/ครีม",
            ["ชมพูเข้ม"] = "ชมเข้ม/ชม/ชม",
            ["วัว/ขาว/แดง"] = "วัว/ขาว /แดง",
            ["น้ำตาล"] = "ตาล"
        };

    public static string NormalizeVariantName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
        return VariantAliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : normalized;
    }

    public static bool IsAllowedColorName(string? value)
    {
        var normalized = NormalizeVariantName(value);
        return ColorNames.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }
}
