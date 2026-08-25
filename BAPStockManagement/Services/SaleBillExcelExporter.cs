using BAPStockManagement.Configuration;
using BAPStockManagement.Helpers;
using BAPStockManagement.Models;
using ClosedXML.Excel;
using Microsoft.Extensions.Options;

namespace BAPStockManagement.Services;

/// <summary>
/// Fills sale bills into the original Excel form template (Templates/BillSaleTemplate.xlsx).
/// </summary>
public class SaleBillExcelExporter
{
    private const int LineStartRow = 14;
    private const int MaxLineRows = 10; // rows 14-23
    private const int LogoWidthPx = 120;
    private const int LogoHeightPx = 80;

    private readonly IWebHostEnvironment _env;
    private readonly BillingOptions _billing;

    public SaleBillExcelExporter(IWebHostEnvironment env, IOptions<BillingOptions> billingOptions)
    {
        _env = env;
        _billing = billingOptions.Value;
    }

    public byte[] Export(IReadOnlyList<SaleBill> bills)
    {
        if (bills.Count == 0)
        {
            throw new ArgumentException("No bills to export.", nameof(bills));
        }

        var templatePath = ResolveTemplatePath();
        var logoPath = ResolveLogoPath();
        using var workbook = new XLWorkbook(templatePath);
        var templateSheet = workbook.Worksheets.First();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < bills.Count; i++)
        {
            var bill = bills[i];
            var sheetName = SanitizeSheetName(bill.BillNo, usedNames, i + 1);
            IXLWorksheet ws;

            if (i == 0)
            {
                templateSheet.Name = sheetName;
                ws = templateSheet;
            }
            else
            {
                ws = templateSheet.CopyTo(sheetName);
            }

            FillBillSheet(ws, bill);
            AddLogo(ws, logoPath);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] Export(SaleBill bill) => Export([bill]);

    private string ResolveTemplatePath()
    {
        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, "Templates", "BillSaleTemplate.xlsx"),
            Path.Combine(AppContext.BaseDirectory, "Templates", "BillSaleTemplate.xlsx")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(
            "ไม่พบเทมเพลตบิลขาย Templates/BillSaleTemplate.xlsx",
            candidates[0]);
    }

    private string ResolveLogoPath()
    {
        var candidates = new[]
        {
            Path.Combine(_env.WebRootPath, "images", "bap-logo.png"),
            Path.Combine(_env.ContentRootPath, "wwwroot", "images", "bap-logo.png"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", "bap-logo.png")
        };

        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(
            "ไม่พบโลโก้ wwwroot/images/bap-logo.png",
            candidates[0]);
    }

    private void FillBillSheet(IXLWorksheet ws, SaleBill bill)
    {
        var lines = bill.Lines.OrderBy(l => l.SortOrder).ToList();
        var gross = lines.Sum(l => l.Qty * l.UnitPrice);
        var discount = lines.Sum(l => l.Qty * l.DiscountPerUnit);
        var net = gross - discount;
        var totalQty = lines.Sum(l => l.Qty);

        // Header / customer (layout matches original sheet "1")
        ws.Cell("C5").Value = bill.CustomerName;
        ws.Cell("C6").Value = bill.CustomerAddress;
        ws.Cell("C7").Value = bill.CustomerDistrict;
        ws.Cell("D8").Value = bill.CustomerProvince;
        ws.Cell("D9").Value = bill.CustomerPhone;
        ws.Cell("D11").Value = bill.CustomerShippingInfo;

        ws.Cell("I4").Value = bill.BillDate.Date;
        ws.Cell("I4").Style.DateFormat.Format = "d/m/yyyy";
        ws.Cell("I5").Value = bill.BillNo;
        ws.Cell("I6").Value = bill.CustomerSalesZone;
        ws.Cell("I8").Value = $"{bill.PaymentTermsDays}  วัน";
        ws.Cell("I9").Value = bill.DueDate.Date;
        ws.Cell("I9").Style.DateFormat.Format = "d/m/yyyy";

        // Clear line input + amount cells (no M/N scratch columns)
        for (var r = LineStartRow; r < LineStartRow + MaxLineRows; r++)
        {
            ws.Cell(r, 2).Clear(XLClearOptions.Contents); // B sku
            ws.Cell(r, 4).Clear(XLClearOptions.Contents); // D description
            ws.Cell(r, 6).Clear(XLClearOptions.Contents); // F qty
            ws.Cell(r, 7).Value = "คัน";
            ws.Cell(r, 8).Clear(XLClearOptions.Contents); // H unit price
            ws.Cell(r, 9).Clear(XLClearOptions.Contents); // I amount
        }

        // Row 24 is unused product line in the original form — clear amount
        ws.Cell(24, 6).Clear(XLClearOptions.Contents);
        ws.Cell(24, 8).Clear(XLClearOptions.Contents);
        ws.Cell(24, 9).Clear(XLClearOptions.Contents);

        for (var i = 0; i < Math.Min(lines.Count, MaxLineRows); i++)
        {
            var line = lines[i];
            var r = LineStartRow + i;
            var lineAmount = line.Qty * line.UnitPrice;

            ws.Cell(r, 2).Value = line.Sku;
            ws.Cell(r, 4).Value = line.Description;
            ws.Cell(r, 6).Value = line.Qty;
            ws.Cell(r, 7).Value = string.IsNullOrWhiteSpace(line.Unit) ? "คัน" : line.Unit;
            ws.Cell(r, 8).Value = line.UnitPrice;
            ws.Cell(r, 9).Value = lineAmount;
            ws.Cell(r, 8).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, 9).Style.NumberFormat.Format = "#,##0.00";
        }

        // Notes / bank details from app settings
        ws.Cell("D27").Value = $"กรุณา สั่งจ่ายเช็คในนาม \"{_billing.ChequePayee}\" และขีดผู้ถือ";
        ws.Cell("D28").Value = $"โอนเงินเข้าบัญชี {_billing.BankAccountKasikorn}";
        ws.Cell("D29").Value = $"โอนเงินเข้าบัญชี {_billing.BankAccountScb}";

        // Totals as values (discount no longer depends on column N)
        ws.Cell("F25").Value = totalQty;
        ws.Cell("I30").Value = gross;
        ws.Cell("I31").Value = discount;
        ws.Cell("I32").Value = net;
        ws.Cell("I30").Style.NumberFormat.Format = "#,##0.00";
        ws.Cell("I31").Style.NumberFormat.Format = "#,##0.00";
        ws.Cell("I32").Style.NumberFormat.Format = "#,##0.00";

        ws.Cell("D32").Value = $"({ThaiBahtText.ToBahtText(net)})";

        if (!string.IsNullOrWhiteSpace(bill.Note))
        {
            var existing = ws.Cell("D11").GetString();
            if (string.IsNullOrWhiteSpace(existing))
            {
                ws.Cell("D11").Value = bill.Note;
            }
        }
    }

    private static void AddLogo(IXLWorksheet ws, string logoPath)
    {
        // Remove any pictures copied from a previous sheet so multi-bill export stays clean
        foreach (var picture in ws.Pictures.ToList())
        {
            picture.Delete();
        }

        ws.AddPicture(logoPath)
            .MoveTo(ws.Cell("A1"), 4, 4)
            .WithSize(LogoWidthPx, LogoHeightPx);
    }

    private static string SanitizeSheetName(string name, HashSet<string> used, int fallbackIndex)
    {
        var cleaned = string.Join("_", name.Split(Path.GetInvalidFileNameChars().Concat([':', '\\', '/', '?', '*', '[', ']']).ToArray()))
            .Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = $"Sheet{fallbackIndex}";
        }

        if (cleaned.Length > 28)
        {
            cleaned = cleaned[..28];
        }

        var candidate = cleaned;
        var n = 2;
        while (!used.Add(candidate))
        {
            var suffix = $"_{n++}";
            candidate = cleaned.Length + suffix.Length > 31
                ? cleaned[..(31 - suffix.Length)] + suffix
                : cleaned + suffix;
        }

        return candidate;
    }
}
