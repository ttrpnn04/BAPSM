using System.ComponentModel.DataAnnotations;
using BAPStockManagement.Constants;

namespace BAPStockManagement.ViewModels.Users;

public class EditUserViewModel
{
    public string Id { get; set; } = string.Empty;

    [Display(Name = "อีเมล")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณาเลือกบทบาท")]
    [Display(Name = "บทบาท")]
    public string Role { get; set; } = AppRoles.Admin;

    [Display(Name = "ปิดการใช้งาน")]
    public bool IsLocked { get; set; }

    [StringLength(100, MinimumLength = 8, ErrorMessage = "รหัสผ่านต้องมีอย่างน้อย {2} ตัวอักษร")]
    [DataType(DataType.Password)]
    [Display(Name = "รหัสผ่านใหม่ (ไม่บังคับ)")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "รหัสผ่านไม่ตรงกัน")]
    [Display(Name = "ยืนยันรหัสผ่านใหม่")]
    public string? ConfirmNewPassword { get; set; }
}
