using System.Globalization;
using System.Text;

namespace BAPStockManagement.Helpers;

public static class ThaiBahtText
{
    private static readonly string[] Ones =
    [
        "", "หนึ่ง", "สอง", "สาม", "สี่", "ห้า", "หก", "เจ็ด", "แปด", "เก้า"
    ];

    private static readonly string[] Places =
    [
        "", "สิบ", "ร้อย", "พัน", "หมื่น", "แสน", "ล้าน"
    ];

    public static string ToBahtText(decimal amount)
    {
        if (amount < 0)
        {
            return "ลบ" + ToBahtText(-amount);
        }

        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var baht = (long)Math.Floor(rounded);
        var satang = (int)Math.Round((rounded - baht) * 100m, MidpointRounding.AwayFromZero);

        if (satang == 100)
        {
            baht += 1;
            satang = 0;
        }

        var sb = new StringBuilder();
        sb.Append(baht == 0 ? "ศูนย์บาท" : ConvertInteger(baht) + "บาท");

        if (satang == 0)
        {
            sb.Append("ถ้วน");
        }
        else
        {
            sb.Append(ConvertInteger(satang));
            sb.Append("สตางค์");
        }

        return sb.ToString();
    }

    private static string ConvertInteger(long number)
    {
        if (number == 0)
        {
            return "ศูนย์";
        }

        var sb = new StringBuilder();
        var text = number.ToString(CultureInfo.InvariantCulture);
        var len = text.Length;

        for (var i = 0; i < len; i++)
        {
            var digit = text[i] - '0';
            var posFromRight = len - i - 1;
            var place = posFromRight % 6;
            var isMillionBoundary = posFromRight > 0 && place == 0;

            if (digit == 0)
            {
                if (isMillionBoundary)
                {
                    sb.Append("ล้าน");
                }

                continue;
            }

            if (place == 1)
            {
                if (digit == 1)
                {
                    sb.Append("สิบ");
                }
                else if (digit == 2)
                {
                    sb.Append("ยี่สิบ");
                }
                else
                {
                    sb.Append(Ones[digit]);
                    sb.Append("สิบ");
                }
            }
            else if (place == 0 && digit == 1 && len > 1 && text[i - 1] != '0')
            {
                sb.Append("เอ็ด");
            }
            else
            {
                sb.Append(Ones[digit]);
                if (place > 0)
                {
                    sb.Append(Places[place]);
                }
            }

            if (isMillionBoundary)
            {
                sb.Append("ล้าน");
            }
        }

        return sb.ToString();
    }
}
