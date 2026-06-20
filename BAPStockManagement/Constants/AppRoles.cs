namespace BAPStockManagement.Constants;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";

    public const string StockView = "SuperAdmin,Admin";

    public const string StockEdit = "SuperAdmin,Admin";

    public const string AdminManage = "SuperAdmin,Admin";

    public static readonly string[] All =
    [
        SuperAdmin,
        Admin
    ];

    public static readonly string[] AssignableBySuperAdmin =
    [
        Admin
    ];
}
