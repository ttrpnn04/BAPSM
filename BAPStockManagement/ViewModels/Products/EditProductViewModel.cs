using System.ComponentModel.DataAnnotations;

namespace BAPStockManagement.ViewModels.Products;

public class EditProductViewModel
{
    public int ProductId { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกหมวดหมู่")]
    [Display(Name = "หมวดหมู่")]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "กรุณากรอก SKU")]
    [MaxLength(50)]
    [Display(Name = "SKU")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกชื่อสินค้า")]
    [MaxLength(300)]
    [Display(Name = "ชื่อสินค้า")]
    public string ProductName { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกหน่วย")]
    [MaxLength(20)]
    [Display(Name = "หน่วย")]
    public string Unit { get; set; } = string.Empty;

    [MaxLength(500)]
    [Display(Name = "หมายเหตุ")]
    public string? Note { get; set; }

    [Display(Name = "ใช้งาน")]
    public bool IsActive { get; set; }

    public IReadOnlyList<ProductVariantItemViewModel> Variants { get; set; } = [];

    [MaxLength(100)]
    [Display(Name = "เพิ่มสี/รุ่นใหม่")]
    public string? NewVariantName { get; set; }
}

public class ProductVariantItemViewModel
{
    public int VariantId { get; set; }

    public string VariantName { get; set; } = string.Empty;

    public int QtyPieces { get; set; }

    public int QtyCases { get; set; }

    public bool IsActive { get; set; }
}
