using System.ComponentModel.DataAnnotations;

namespace BAPStockManagement.ViewModels.Categories;

public class CategoryFormViewModel
{
    public int? CategoryId { get; set; }

    [Required(ErrorMessage = "กรุณากรอกชื่อหมวดหมู่")]
    [MaxLength(150)]
    [Display(Name = "ชื่อหมวดหมู่")]
    public string CategoryName { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกชื่อคอลัมน์ variant")]
    [MaxLength(50)]
    [Display(Name = "ชื่อคอลัมน์สี/รุ่น")]
    public string VariantLabel { get; set; } = "สี";

    [Display(Name = "ลำดับ")]
    public int SortOrder { get; set; }

    [Display(Name = "ใช้งาน")]
    public bool IsActive { get; set; } = true;
}

public class CategoryListItemViewModel
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public string VariantLabel { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public int ProductCount { get; set; }

    public bool IsActive { get; set; }
}
