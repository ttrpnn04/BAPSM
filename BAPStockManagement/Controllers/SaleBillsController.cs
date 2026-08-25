using BAPStockManagement.Configuration;
using BAPStockManagement.Constants;
using BAPStockManagement.Helpers;
using BAPStockManagement.Models;
using BAPStockManagement.Services;
using BAPStockManagement.ViewModels.SaleBills;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.StockView)]
public class SaleBillsController : Controller
{
    private readonly BAPStockContext _context;
    private readonly BillingOptions _billing;
    private readonly SaleBillService _saleBills;
    private readonly SaleBillExcelExporter _excelExporter;

    public SaleBillsController(
        BAPStockContext context,
        IOptions<BillingOptions> billingOptions,
        SaleBillService saleBills,
        SaleBillExcelExporter excelExporter)
    {
        _context = context;
        _billing = billingOptions.Value;
        _saleBills = saleBills;
        _excelExporter = excelExporter;
    }

    public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate, string? search)
    {
        var from = (fromDate ?? DateTime.Today.AddDays(-30)).Date;
        var to = (toDate ?? DateTime.Today).Date;

        var query = _context.SaleBills
            .AsNoTracking()
            .Include(b => b.Lines)
            .Where(b => b.BillDate >= from && b.BillDate <= to);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b =>
                b.BillNo.Contains(term) ||
                b.CustomerName.Contains(term));
        }

        var bills = await query
            .OrderByDescending(b => b.BillDate)
            .ThenByDescending(b => b.SaleBillId)
            .Take(200)
            .ToListAsync();

        var items = bills.Select(b => new SaleBillListItemViewModel
        {
            SaleBillId = b.SaleBillId,
            BillNo = b.BillNo,
            BillDate = b.BillDate,
            CustomerName = b.CustomerName,
            TotalQty = b.Lines.Sum(l => l.Qty),
            GrossAmount = b.Lines.Sum(l => l.Qty * l.UnitPrice),
            DiscountAmount = b.Lines.Sum(l => l.Qty * l.DiscountPerUnit),
            NetAmount = b.Lines.Sum(l => l.Qty * l.UnitPrice - l.Qty * l.DiscountPerUnit)
        }).ToList();

        return View(new SaleBillIndexViewModel
        {
            FromDate = from,
            ToDate = to,
            Search = search,
            Items = items
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new SaleBillFormViewModel
        {
            BillDate = DateTime.Today,
            PaymentTermsDays = _billing.DefaultPaymentTermsDays,
            Lines =
            [
                new SaleBillLineFormViewModel { Unit = "คัน" },
                new SaleBillLineFormViewModel { Unit = "คัน" },
                new SaleBillLineFormViewModel { Unit = "คัน" }
            ]
        };
        await PopulateCustomersAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SaleBillFormViewModel model)
    {
        NormalizeLines(model);
        await ValidateBillAsync(model);

        if (!ModelState.IsValid)
        {
            await PopulateCustomersAsync(model);
            return View(model);
        }

        var customer = await _context.Customers.FirstAsync(c => c.CustomerId == model.CustomerId!.Value);
        var billDate = model.BillDate.Date;
        var bill = new SaleBill
        {
            BillNo = await _saleBills.GenerateBillNoAsync(billDate),
            BillDate = billDate,
            CustomerId = customer.CustomerId,
            PaymentTermsDays = model.PaymentTermsDays,
            DueDate = billDate.AddDays(model.PaymentTermsDays),
            CustomerName = customer.Name,
            CustomerAddress = customer.Address,
            CustomerDistrict = customer.District,
            CustomerProvince = customer.Province,
            CustomerPhone = customer.Phone1,
            CustomerSalesZone = customer.SalesZone,
            CustomerShippingInfo = customer.ShippingInfo,
            Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim(),
            CreatedAt = DateTime.Now,
            CreatedBy = User.Identity?.Name,
            Lines = model.Lines
                .Where(IsFilledLine)
                .Select((l, i) => new SaleBillLine
                {
                    Sku = string.IsNullOrWhiteSpace(l.Sku) ? null : l.Sku.Trim(),
                    Description = l.Description.Trim(),
                    Qty = l.Qty,
                    Unit = string.IsNullOrWhiteSpace(l.Unit) ? "คัน" : l.Unit.Trim(),
                    UnitPrice = l.UnitPrice,
                    DiscountPerUnit = l.DiscountPerUnit,
                    SortOrder = i + 1
                })
                .ToList()
        };

        _context.SaleBills.Add(bill);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"สร้างบิล {bill.BillNo} สำเร็จ";
        return RedirectToAction(nameof(Details), new { id = bill.SaleBillId });
    }

    public async Task<IActionResult> Details(int id)
    {
        var vm = await BuildDetailAsync(id);
        if (vm == null)
        {
            return NotFound();
        }

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadExcel(int id)
    {
        var bill = await _context.SaleBills
            .AsNoTracking()
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.SaleBillId == id);
        if (bill == null)
        {
            return NotFound();
        }

        try
        {
            var bytes = _excelExporter.Export(bill);
            var fileName = $"{bill.BillNo}_{DateTime.Now:yyyyMMdd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (FileNotFoundException)
        {
            TempData["Error"] = "ไม่พบไฟล์เทมเพลตบิลขาย";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var bill = await _context.SaleBills
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.SaleBillId == id);
        if (bill == null)
        {
            return NotFound();
        }

        var model = new SaleBillFormViewModel
        {
            SaleBillId = bill.SaleBillId,
            CustomerId = bill.CustomerId,
            BillDate = bill.BillDate,
            PaymentTermsDays = bill.PaymentTermsDays,
            Note = bill.Note,
            CustomerPreviewName = bill.CustomerName,
            CustomerPreviewAddress = FormatAddress(bill.CustomerAddress, bill.CustomerDistrict, bill.CustomerProvince),
            CustomerPreviewPhone = bill.CustomerPhone,
            CustomerPreviewShipping = bill.CustomerShippingInfo,
            Lines = bill.Lines
                .OrderBy(l => l.SortOrder)
                .Select(l => new SaleBillLineFormViewModel
                {
                    Sku = l.Sku,
                    Description = l.Description,
                    Qty = l.Qty,
                    Unit = l.Unit,
                    UnitPrice = l.UnitPrice,
                    DiscountPerUnit = l.DiscountPerUnit
                })
                .ToList()
        };

        if (model.Lines.Count == 0)
        {
            model.Lines.Add(new SaleBillLineFormViewModel { Unit = "คัน" });
        }

        await PopulateCustomersAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SaleBillFormViewModel model)
    {
        if (!model.SaleBillId.HasValue)
        {
            return NotFound();
        }

        NormalizeLines(model);
        await ValidateBillAsync(model);

        if (!ModelState.IsValid)
        {
            await PopulateCustomersAsync(model);
            return View(model);
        }

        var bill = await _context.SaleBills
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.SaleBillId == model.SaleBillId.Value);
        if (bill == null)
        {
            return NotFound();
        }

        var customer = await _context.Customers.FirstAsync(c => c.CustomerId == model.CustomerId!.Value);
        var billDate = model.BillDate.Date;

        bill.BillDate = billDate;
        bill.CustomerId = customer.CustomerId;
        bill.PaymentTermsDays = model.PaymentTermsDays;
        bill.DueDate = billDate.AddDays(model.PaymentTermsDays);
        bill.CustomerName = customer.Name;
        bill.CustomerAddress = customer.Address;
        bill.CustomerDistrict = customer.District;
        bill.CustomerProvince = customer.Province;
        bill.CustomerPhone = customer.Phone1;
        bill.CustomerSalesZone = customer.SalesZone;
        bill.CustomerShippingInfo = customer.ShippingInfo;
        bill.Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();

        _context.SaleBillLines.RemoveRange(bill.Lines);
        bill.Lines = model.Lines
            .Where(IsFilledLine)
            .Select((l, i) => new SaleBillLine
            {
                Sku = string.IsNullOrWhiteSpace(l.Sku) ? null : l.Sku.Trim(),
                Description = l.Description.Trim(),
                Qty = l.Qty,
                Unit = string.IsNullOrWhiteSpace(l.Unit) ? "คัน" : l.Unit.Trim(),
                UnitPrice = l.UnitPrice,
                DiscountPerUnit = l.DiscountPerUnit,
                SortOrder = i + 1
            })
            .ToList();

        await _context.SaveChangesAsync();

        TempData["Success"] = $"บันทึกบิล {bill.BillNo} สำเร็จ";
        return RedirectToAction(nameof(Details), new { id = bill.SaleBillId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var bill = await _context.SaleBills.FindAsync(id);
        if (bill == null)
        {
            return NotFound();
        }

        var billNo = bill.BillNo;
        _context.SaleBills.Remove(bill);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"ลบบิล {billNo} แล้ว";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> PrintBill(int id)
    {
        var vm = await BuildPrintAsync(id);
        if (vm == null)
        {
            return NotFound();
        }

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintBoxLabel(int id, int count = 5)
    {
        var vm = await BuildPrintAsync(id);
        if (vm == null)
        {
            return NotFound();
        }

        vm.BoxLabelCount = Math.Clamp(count, 1, 30);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintEnvelope(int id)
    {
        var vm = await BuildPrintAsync(id);
        if (vm == null)
        {
            return NotFound();
        }

        return View(vm);
    }

    private async Task ValidateBillAsync(SaleBillFormViewModel model)
    {
        // Drop automatic line-field errors first; we validate filled rows only.
        for (var i = 0; i < model.Lines.Count; i++)
        {
            ClearLineErrors(i);
        }

        // BillDate often comes from Flatpickr; make failure message clear in Thai.
        if (ModelState.ContainsKey(nameof(model.BillDate)) &&
            ModelState[nameof(model.BillDate)]!.Errors.Count > 0)
        {
            ModelState.Remove(nameof(model.BillDate));
            ModelState.AddModelError(nameof(model.BillDate), "กรุณาเลือกวันที่บิลให้ถูกต้อง");
        }
        else if (model.BillDate < new DateTime(2000, 1, 1))
        {
            ModelState.AddModelError(nameof(model.BillDate), "กรุณาเลือกวันที่บิลให้ถูกต้อง");
        }

        if (!model.CustomerId.HasValue)
        {
            ModelState.AddModelError(nameof(model.CustomerId), "กรุณาเลือกร้าน");
        }
        else
        {
            var exists = await _context.Customers.AnyAsync(c => c.CustomerId == model.CustomerId.Value && c.IsActive);
            if (!exists)
            {
                ModelState.AddModelError(nameof(model.CustomerId), "ไม่พบร้านที่เลือก หรือร้านถูกปิดใช้งาน");
            }
        }

        var filled = model.Lines.Where(IsFilledLine).ToList();
        if (filled.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "กรุณากรอกรายการสินค้าอย่างน้อย 1 รายการ");
        }

        for (var i = 0; i < model.Lines.Count; i++)
        {
            var line = model.Lines[i];
            var hasAny = !string.IsNullOrWhiteSpace(line.Sku)
                || !string.IsNullOrWhiteSpace(line.Description)
                || line.Qty > 0
                || line.UnitPrice > 0
                || line.DiscountPerUnit > 0;

            if (!hasAny)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.Description))
            {
                ModelState.AddModelError($"Lines[{i}].Description", "กรุณากรอกรายการ");
            }

            if (line.Qty <= 0)
            {
                ModelState.AddModelError($"Lines[{i}].Qty", "จำนวนต้องมากกว่า 0");
            }

            if (line.UnitPrice < 0)
            {
                ModelState.AddModelError($"Lines[{i}].UnitPrice", "ราคาต้องไม่ติดลบ");
            }

            if (line.DiscountPerUnit < 0)
            {
                ModelState.AddModelError($"Lines[{i}].DiscountPerUnit", "ส่วนลดต้องไม่ติดลบ");
            }
        }
    }

    private void ClearLineErrors(int index)
    {
        var keys = ModelState.Keys
            .Where(k => k.StartsWith($"Lines[{index}]", StringComparison.Ordinal))
            .ToList();
        foreach (var key in keys)
        {
            ModelState.Remove(key);
        }
    }

    private static void NormalizeLines(SaleBillFormViewModel model)
    {
        model.Lines ??= [];
        while (model.Lines.Count < 1)
        {
            model.Lines.Add(new SaleBillLineFormViewModel { Unit = "คัน" });
        }
    }

    private static bool IsFilledLine(SaleBillLineFormViewModel line) =>
        !string.IsNullOrWhiteSpace(line.Description) && line.Qty > 0;

    private async Task PopulateCustomersAsync(SaleBillFormViewModel model)
    {
        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { c.CustomerId, c.CustomerCode, c.Name, c.District, c.Province })
            .ToListAsync();

        model.CustomerOptions = customers.Select(c => new SelectListItem
        {
            Value = c.CustomerId.ToString(),
            Text = $"{c.CustomerCode} — {c.Name}" +
                   (string.IsNullOrWhiteSpace(c.District) && string.IsNullOrWhiteSpace(c.Province)
                       ? string.Empty
                       : $" ({c.District} {c.Province})".TrimEnd()),
            Selected = model.CustomerId == c.CustomerId
        });

        if (model.CustomerId.HasValue)
        {
            var selected = await _context.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustomerId == model.CustomerId.Value);
            if (selected != null)
            {
                model.CustomerPreviewName = selected.Name;
                model.CustomerPreviewAddress = FormatAddress(selected.Address, selected.District, selected.Province);
                model.CustomerPreviewPhone = selected.Phone1;
                model.CustomerPreviewShipping = selected.ShippingInfo;
            }
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportExcel(int[]? ids, DateTime? fromDate, DateTime? toDate)
    {
        List<SaleBill> bills;

        if (ids is { Length: > 0 })
        {
            bills = await _context.SaleBills
                .AsNoTracking()
                .Include(b => b.Lines)
                .Where(b => ids.Contains(b.SaleBillId))
                .OrderBy(b => b.BillDate)
                .ThenBy(b => b.SaleBillId)
                .ToListAsync();
        }
        else
        {
            var from = (fromDate ?? DateTime.Today.AddDays(-30)).Date;
            var to = (toDate ?? DateTime.Today).Date;
            bills = await _context.SaleBills
                .AsNoTracking()
                .Include(b => b.Lines)
                .Where(b => b.BillDate >= from && b.BillDate <= to)
                .OrderBy(b => b.BillDate)
                .ThenBy(b => b.SaleBillId)
                .Take(50)
                .ToListAsync();
        }

        if (bills.Count == 0)
        {
            TempData["Error"] = "ไม่พบบิลสำหรับ Export";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var bytes = _excelExporter.Export(bills);
            var fileName = $"BAP_Bills_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (FileNotFoundException)
        {
            TempData["Error"] = "ไม่พบไฟล์เทมเพลตบิลขาย";
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task<SaleBillDetailViewModel?> BuildDetailAsync(int id)
    {
        var bill = await _context.SaleBills
            .AsNoTracking()
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.SaleBillId == id);
        if (bill == null)
        {
            return null;
        }

        var vm = new SaleBillDetailViewModel
        {
            SaleBillId = bill.SaleBillId,
            BillNo = bill.BillNo,
            BillDate = bill.BillDate,
            DueDate = bill.DueDate,
            PaymentTermsDays = bill.PaymentTermsDays,
            CustomerId = bill.CustomerId,
            CustomerName = bill.CustomerName,
            CustomerAddress = bill.CustomerAddress,
            CustomerDistrict = bill.CustomerDistrict,
            CustomerProvince = bill.CustomerProvince,
            CustomerPhone = bill.CustomerPhone,
            CustomerSalesZone = bill.CustomerSalesZone,
            CustomerShippingInfo = bill.CustomerShippingInfo,
            Note = bill.Note,
            CreatedBy = bill.CreatedBy,
            CreatedAt = bill.CreatedAt,
            Lines = bill.Lines
                .OrderBy(l => l.SortOrder)
                .Select(l => new SaleBillLineDetailViewModel
                {
                    Sku = l.Sku,
                    Description = l.Description,
                    Qty = l.Qty,
                    Unit = l.Unit,
                    UnitPrice = l.UnitPrice,
                    DiscountPerUnit = l.DiscountPerUnit
                })
                .ToList()
        };

        vm.BahtText = ThaiBahtText.ToBahtText(vm.NetAmount);
        return vm;
    }

    private async Task<SaleBillPrintViewModel?> BuildPrintAsync(int id)
    {
        var detail = await BuildDetailAsync(id);
        if (detail == null)
        {
            return null;
        }

        return new SaleBillPrintViewModel
        {
            SaleBillId = detail.SaleBillId,
            BillNo = detail.BillNo,
            BillDate = detail.BillDate,
            DueDate = detail.DueDate,
            PaymentTermsDays = detail.PaymentTermsDays,
            CustomerId = detail.CustomerId,
            CustomerName = detail.CustomerName,
            CustomerAddress = detail.CustomerAddress,
            CustomerDistrict = detail.CustomerDistrict,
            CustomerProvince = detail.CustomerProvince,
            CustomerPhone = detail.CustomerPhone,
            CustomerSalesZone = detail.CustomerSalesZone,
            CustomerShippingInfo = detail.CustomerShippingInfo,
            Note = detail.Note,
            CreatedBy = detail.CreatedBy,
            CreatedAt = detail.CreatedAt,
            Lines = detail.Lines,
            BahtText = detail.BahtText,
            ChequePayee = _billing.ChequePayee,
            BankAccountKasikorn = _billing.BankAccountKasikorn,
            BankAccountScb = _billing.BankAccountScb,
            SenderName = _billing.SenderName,
            SenderPhone = _billing.SenderPhone
        };
    }

    private static string? FormatAddress(string? address, string? district, string? province)
    {
        var parts = new[] { address, district, province }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());
        var joined = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
