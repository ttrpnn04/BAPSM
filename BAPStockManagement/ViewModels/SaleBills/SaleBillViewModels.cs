using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BAPStockManagement.ViewModels.SaleBills;

public class SaleBillIndexViewModel
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public string? Search { get; set; }

    public List<SaleBillListItemViewModel> Items { get; set; } = [];
}

public class SaleBillListItemViewModel
{
    public int SaleBillId { get; set; }

    public string BillNo { get; set; } = string.Empty;

    public DateTime BillDate { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public decimal TotalQty { get; set; }

    public decimal GrossAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal NetAmount { get; set; }
}

public class SaleBillLineFormViewModel
{
    [MaxLength(50)]
    [Display(Name = "รหัสสินค้า")]
    public string? Sku { get; set; }

    // Validation for filled lines is done in SaleBillsController.ValidateBillAsync
    // so empty rows do not block ModelState.
    [MaxLength(300)]
    [Display(Name = "รายการ")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "จำนวน")]
    public decimal Qty { get; set; }

    [MaxLength(20)]
    [Display(Name = "หน่วย")]
    public string Unit { get; set; } = "คัน";

    [Display(Name = "ราคาต่อหน่วย")]
    public decimal UnitPrice { get; set; }

    [Display(Name = "ส่วนลดต่อหน่วย")]
    public decimal DiscountPerUnit { get; set; }
}

public class SaleBillFormViewModel
{
    public int? SaleBillId { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกร้าน")]
    [Display(Name = "ร้าน / ลูกค้า")]
    public int? CustomerId { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกวันที่บิล")]
    [Display(Name = "วันที่")]
    [DataType(DataType.Date)]
    public DateTime BillDate { get; set; } = DateTime.Today;

    [Range(0, 365)]
    [Display(Name = "เงื่อนไขชำระ (วัน)")]
    public int PaymentTermsDays { get; set; } = 30;

    [MaxLength(500)]
    [Display(Name = "หมายเหตุ")]
    public string? Note { get; set; }

    public List<SaleBillLineFormViewModel> Lines { get; set; } =
    [
        new SaleBillLineFormViewModel()
    ];

    public IEnumerable<SelectListItem> CustomerOptions { get; set; } = [];

    public string? CustomerPreviewName { get; set; }

    public string? CustomerPreviewAddress { get; set; }

    public string? CustomerPreviewPhone { get; set; }

    public string? CustomerPreviewShipping { get; set; }
}

public class SaleBillDetailViewModel
{
    public int SaleBillId { get; set; }

    public string BillNo { get; set; } = string.Empty;

    public DateTime BillDate { get; set; }

    public DateTime DueDate { get; set; }

    public int PaymentTermsDays { get; set; }

    public int CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string? CustomerAddress { get; set; }

    public string? CustomerDistrict { get; set; }

    public string? CustomerProvince { get; set; }

    public string? CustomerPhone { get; set; }

    public string? CustomerSalesZone { get; set; }

    public string? CustomerShippingInfo { get; set; }

    public string? Note { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<SaleBillLineDetailViewModel> Lines { get; set; } = [];

    public decimal TotalQty => Lines.Sum(l => l.Qty);

    public decimal GrossAmount => Lines.Sum(l => l.LineAmount);

    public decimal DiscountAmount => Lines.Sum(l => l.LineDiscountAmount);

    public decimal NetAmount => GrossAmount - DiscountAmount;

    public string BahtText { get; set; } = string.Empty;
}

public class SaleBillLineDetailViewModel
{
    public string? Sku { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Qty { get; set; }

    public string Unit { get; set; } = "คัน";

    public decimal UnitPrice { get; set; }

    public decimal DiscountPerUnit { get; set; }

    public decimal LineAmount => Qty * UnitPrice;

    public decimal LineDiscountAmount => Qty * DiscountPerUnit;
}

public class SaleBillPrintViewModel : SaleBillDetailViewModel
{
    public string ChequePayee { get; set; } = string.Empty;

    public string BankAccountKasikorn { get; set; } = string.Empty;

    public string BankAccountScb { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string SenderPhone { get; set; } = string.Empty;

    public int BoxLabelCount { get; set; } = 5;
}
