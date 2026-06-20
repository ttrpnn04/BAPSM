namespace BAPStockManagement.Constants;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Viewer = "Viewer";

    public const string StockView = "SuperAdmin,Admin,Staff,Viewer";

    public const string StockEdit = "SuperAdmin,Admin,Staff";

    public const string AdminManage = "SuperAdmin,Admin";

    public static readonly string[] All =
    [
        SuperAdmin,
        Admin,
        Staff,
        Viewer
    ];

    public static readonly string[] AssignableBySuperAdmin =
    [
        Admin,
        Staff,
        Viewer
    ];
}
