using System.ComponentModel.DataAnnotations;
using BAPStockManagement.Constants;

namespace BAPStockManagement.ViewModels.Products;

public class CreateProductViewModel
{
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
    public string Unit { get; set; } = "คัน";

    [MaxLength(500)]
    [Display(Name = "หมายเหตุ")]
    public string? Note { get; set; }

    [Required(ErrorMessage = "กรุณาระบุสี/รุ่นอย่างน้อย 1 รายการ")]
    [Display(Name = "สี/รุ่น (หนึ่งบรรทัดต่อหนึ่งรายการ)")]
    public string VariantsText { get; set; } = string.Join(Environment.NewLine, ProductVariantDefaults.ColorNames);
}
