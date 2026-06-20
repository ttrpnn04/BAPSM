namespace BAPStockManagement.Configuration;

public class SeedSuperAdminOptions
{
    public const string SectionName = "SeedSuperAdmin";

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "ผู้ดูแลระบบ";
}
