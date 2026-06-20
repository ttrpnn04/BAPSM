using BAPStockManagement.Configuration;
using BAPStockManagement.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BAPStockManagement.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var options = services.GetRequiredService<IOptions<SeedSuperAdminOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");

        foreach (var roleName in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        if (await userManager.Users.AnyAsync())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            logger.LogWarning(
                "ยังไม่มีผู้ใช้ในระบบ กรุณาตั้งค่า SeedSuperAdmin:Email และ SeedSuperAdmin:Password ใน config");
            return;
        }

        var user = new IdentityUser
        {
            UserName = options.Email.Trim(),
            Email = options.Email.Trim(),
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, options.Password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "สร้าง SuperAdmin ไม่สำเร็จ: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, AppRoles.SuperAdmin);
        logger.LogInformation("สร้าง SuperAdmin เริ่มต้น ({Email}) สำเร็จ", options.Email);
    }
}
