using System.ComponentModel.DataAnnotations;

namespace BAPStockManagement.ViewModels.Customers;

public class CustomerListItemViewModel
{
    public int CustomerId { get; set; }

    public int CustomerCode { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? District { get; set; }

    public string? Province { get; set; }

    public string? Phone1 { get; set; }

    public string? SalesZone { get; set; }

    public bool IsActive { get; set; }
}

public class CustomerIndexViewModel
{
    public string? Search { get; set; }

    public bool ShowInactive { get; set; }

    public List<CustomerListItemViewModel> Items { get; set; } = [];
}

public class CustomerFormViewModel
{
    public int? CustomerId { get; set; }

    [Required(ErrorMessage = "กรุณากรอกรหัสลูกค้า")]
    [Display(Name = "รหัสลูกค้า")]
    public int CustomerCode { get; set; }

    [Required(ErrorMessage = "กรุณากรอกชื่อร้าน")]
    [MaxLength(300)]
    [Display(Name = "ชื่อร้าน / ลูกค้า")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    [Display(Name = "ที่อยู่")]
    public string? Address { get; set; }

    [MaxLength(100)]
    [Display(Name = "อำเภอ")]
    public string? District { get; set; }

    [MaxLength(100)]
    [Display(Name = "จังหวัด")]
    public string? Province { get; set; }

    [MaxLength(50)]
    [Display(Name = "เบอร์โทร 1")]
    public string? Phone1 { get; set; }

    [MaxLength(50)]
    [Display(Name = "เบอร์โทร 2")]
    public string? Phone2 { get; set; }

    [MaxLength(50)]
    [Display(Name = "เขตการขาย")]
    public string? SalesZone { get; set; }

    [MaxLength(50)]
    [Display(Name = "เลขผู้เสียภาษี")]
    public string? TaxId { get; set; }

    [MaxLength(500)]
    [Display(Name = "ขนส่ง")]
    public string? ShippingInfo { get; set; }

    [Display(Name = "วงเงิน")]
    public decimal CreditLimit { get; set; }

    [Display(Name = "ใช้งาน")]
    public bool IsActive { get; set; } = true;
}

public class CustomerImportViewModel
{
    [Required(ErrorMessage = "กรุณาเลือกไฟล์ Excel")]
    [Display(Name = "ไฟล์ Excel บิลขาย / ใบปะ")]
    public IFormFile? File { get; set; }
}
