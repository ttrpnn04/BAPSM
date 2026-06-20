using System.ComponentModel.DataAnnotations;
using BAPStockManagement.Constants;

namespace BAPStockManagement.ViewModels.Users;

public class CreateUserViewModel
{
    [Required(ErrorMessage = "กรุณากรอกอีเมล")]
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    [Display(Name = "อีเมล")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกรหัสผ่าน")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "รหัสผ่านต้องมีอย่างน้อย {2} ตัวอักษร")]
    [DataType(DataType.Password)]
    [Display(Name = "รหัสผ่าน")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณายืนยันรหัสผ่าน")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "รหัสผ่านไม่ตรงกัน")]
    [Display(Name = "ยืนยันรหัสผ่าน")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณาเลือกบทบาท")]
    [Display(Name = "บทบาท")]
    public string Role { get; set; } = AppRoles.Admin;
}
