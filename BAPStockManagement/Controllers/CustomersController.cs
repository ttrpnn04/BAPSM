using BAPStockManagement.Constants;
using BAPStockManagement.Models;
using BAPStockManagement.ViewModels.Customers;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.StockView)]
public class CustomersController : Controller
{
    private readonly BAPStockContext _context;

    public CustomersController(BAPStockContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search, bool showInactive = false)
    {
        var query = _context.Customers.AsNoTracking().AsQueryable();

        if (!showInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                c.Name.Contains(term) ||
                c.CustomerCode.ToString().Contains(term) ||
                (c.District != null && c.District.Contains(term)) ||
                (c.Province != null && c.Province.Contains(term)) ||
                (c.Phone1 != null && c.Phone1.Contains(term)));
        }

        var items = await query
            .OrderBy(c => c.IsActive ? 0 : 1)
            .ThenBy(c => c.CustomerCode)
            .Select(c => new CustomerListItemViewModel
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                Name = c.Name,
                District = c.District,
                Province = c.Province,
                Phone1 = c.Phone1,
                SalesZone = c.SalesZone,
                IsActive = c.IsActive
            })
            .Take(500)
            .ToListAsync();

        return View(new CustomerIndexViewModel
        {
            Search = search,
            ShowInactive = showInactive,
            Items = items
        });
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpGet]
    public IActionResult Create()
    {
        return View(new CustomerFormViewModel());
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var codeExists = await _context.Customers.AnyAsync(c => c.CustomerCode == model.CustomerCode);
        if (codeExists)
        {
            ModelState.AddModelError(nameof(model.CustomerCode), "รหัสลูกค้านี้มีอยู่แล้ว");
            return View(model);
        }

        var now = DateTime.Now;
        var customer = MapToEntity(model, new Customer
        {
            CreatedAt = now,
            UpdatedAt = now
        });

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"เพิ่มลูกค้า {customer.Name} สำเร็จ";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null)
        {
            return NotFound();
        }

        return View(MapToForm(customer));
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CustomerFormViewModel model)
    {
        if (!model.CustomerId.HasValue)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var customer = await _context.Customers.FindAsync(model.CustomerId.Value);
        if (customer == null)
        {
            return NotFound();
        }

        var codeExists = await _context.Customers.AnyAsync(c =>
            c.CustomerId != model.CustomerId.Value &&
            c.CustomerCode == model.CustomerCode);
        if (codeExists)
        {
            ModelState.AddModelError(nameof(model.CustomerCode), "รหัสลูกค้านี้มีอยู่แล้ว");
            return View(model);
        }

        MapToEntity(model, customer);
        customer.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();

        TempData["Success"] = $"บันทึกลูกค้า {customer.Name} สำเร็จ";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpGet]
    public IActionResult Import()
    {
        return View(new CustomerImportViewModel());
    }

    [Authorize(Roles = AppRoles.AdminManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(CustomerImportViewModel model)
    {
        if (model.File == null || model.File.Length == 0)
        {
            ModelState.AddModelError(nameof(model.File), "กรุณาเลือกไฟล์ Excel");
            return View(model);
        }

        var ext = Path.GetExtension(model.File.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xlsm")
        {
            ModelState.AddModelError(nameof(model.File), "รองรับเฉพาะไฟล์ .xlsx");
            return View(model);
        }

        await using var stream = new MemoryStream();
        await model.File.CopyToAsync(stream);
        stream.Position = 0;

        int inserted;
        int updated;
        try
        {
            (inserted, updated) = await ImportCustomersFromExcelAsync(stream);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"อ่านไฟล์ไม่สำเร็จ: {ex.Message}");
            return View(model);
        }

        TempData["Success"] = $"นำเข้าลูกค้าสำเร็จ: เพิ่ม {inserted} รายการ, อัปเดต {updated} รายการ";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Lookup(int id)
    {
        var customer = await _context.Customers
            .AsNoTracking()
            .Where(c => c.CustomerId == id && c.IsActive)
            .Select(c => new
            {
                c.CustomerId,
                c.CustomerCode,
                c.Name,
                c.Address,
                c.District,
                c.Province,
                c.Phone1,
                c.Phone2,
                c.SalesZone,
                c.ShippingInfo
            })
            .FirstOrDefaultAsync();

        if (customer == null)
        {
            return NotFound();
        }

        return Json(customer);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q)
    {
        var term = (q ?? string.Empty).Trim();
        var query = _context.Customers.AsNoTracking().Where(c => c.IsActive);

        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(c =>
                c.Name.Contains(term) ||
                c.CustomerCode.ToString().Contains(term));
        }

        var items = await query
            .OrderBy(c => c.Name)
            .Take(30)
            .Select(c => new
            {
                c.CustomerId,
                c.CustomerCode,
                c.Name,
                c.District,
                c.Province
            })
            .ToListAsync();

        return Json(items);
    }

    private async Task<(int Inserted, int Updated)> ImportCustomersFromExcelAsync(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets
            .FirstOrDefault(ws =>
                string.Equals(ws.Name, "customer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ws.Name, "customer_BAP", StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheet(1);

        var existing = await _context.Customers.ToDictionaryAsync(c => c.CustomerCode);
        var inserted = 0;
        var updated = 0;
        var now = DateTime.Now;
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 2;

        for (var row = 3; row <= lastRow; row++)
        {
            var codeCell = worksheet.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(codeCell) || !int.TryParse(codeCell, out var code))
            {
                var numeric = worksheet.Cell(row, 1).Value;
                if (!numeric.IsNumber)
                {
                    continue;
                }

                code = (int)numeric.GetNumber();
            }

            var name = worksheet.Cell(row, 3).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!existing.TryGetValue(code, out var customer))
            {
                customer = new Customer
                {
                    CustomerCode = code,
                    CreatedAt = now,
                    IsActive = true
                };
                _context.Customers.Add(customer);
                existing[code] = customer;
                inserted++;
            }
            else
            {
                updated++;
            }

            customer.Name = Truncate(name, 300)!;
            customer.Address = Truncate(NullIfEmpty(worksheet.Cell(row, 4).GetString()), 500);
            customer.District = Truncate(NullIfEmpty(worksheet.Cell(row, 5).GetString()), 100);
            customer.Province = Truncate(NullIfEmpty(worksheet.Cell(row, 6).GetString()), 100);
            customer.Phone1 = Truncate(NullIfEmpty(worksheet.Cell(row, 7).GetString()), 50);
            customer.Phone2 = Truncate(NullIfEmpty(worksheet.Cell(row, 8).GetString()), 50);
            customer.SalesZone = Truncate(NullIfEmpty(ReadCellAsText(worksheet.Cell(row, 9))), 50);
            customer.TaxId = Truncate(NullIfEmpty(worksheet.Cell(row, 10).GetString().Trim().TrimStart('\'')), 50);
            customer.ShippingInfo = Truncate(NullIfEmpty(worksheet.Cell(row, 11).GetString()), 500);
            customer.CreditLimit = ReadDecimal(worksheet.Cell(row, 12));
            customer.UpdatedAt = now;
            customer.IsActive = true;
        }

        await _context.SaveChangesAsync();
        return (inserted, updated);
    }

    private static Customer MapToEntity(CustomerFormViewModel model, Customer customer)
    {
        customer.CustomerCode = model.CustomerCode;
        customer.Name = model.Name.Trim();
        customer.Address = NullIfEmpty(model.Address);
        customer.District = NullIfEmpty(model.District);
        customer.Province = NullIfEmpty(model.Province);
        customer.Phone1 = NullIfEmpty(model.Phone1);
        customer.Phone2 = NullIfEmpty(model.Phone2);
        customer.SalesZone = NullIfEmpty(model.SalesZone);
        customer.TaxId = NullIfEmpty(model.TaxId);
        customer.ShippingInfo = NullIfEmpty(model.ShippingInfo);
        customer.CreditLimit = model.CreditLimit;
        customer.IsActive = model.IsActive;
        return customer;
    }

    private static CustomerFormViewModel MapToForm(Customer customer) => new()
    {
        CustomerId = customer.CustomerId,
        CustomerCode = customer.CustomerCode,
        Name = customer.Name,
        Address = customer.Address,
        District = customer.District,
        Province = customer.Province,
        Phone1 = customer.Phone1,
        Phone2 = customer.Phone2,
        SalesZone = customer.SalesZone,
        TaxId = customer.TaxId,
        ShippingInfo = customer.ShippingInfo,
        CreditLimit = customer.CreditLimit,
        IsActive = customer.IsActive
    };

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? Truncate(string? value, int max)
    {
        if (value == null)
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }

    private static string ReadCellAsText(IXLCell cell)
    {
        if (cell.Value.IsNumber)
        {
            return ((long)cell.Value.GetNumber()).ToString();
        }

        return cell.GetString().Trim();
    }

    private static decimal ReadDecimal(IXLCell cell)
    {
        if (cell.Value.IsNumber)
        {
            return (decimal)cell.Value.GetNumber();
        }

        return decimal.TryParse(cell.GetString().Trim(), out var value) ? value : 0m;
    }
}
