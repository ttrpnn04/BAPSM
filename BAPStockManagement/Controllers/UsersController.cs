using BAPStockManagement.Constants;
using BAPStockManagement.ViewModels.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.SuperAdmin)]
public class UsersController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UsersController(
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var model = new List<UserListItemViewModel>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            model.Add(new UserListItemViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? string.Empty,
                Role = roles.FirstOrDefault() ?? "-",
                IsLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow
            });
        }

        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.Roles = GetAssignableRoles();
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        ViewBag.Roles = GetAssignableRoles();

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!AppRoles.AssignableBySuperAdmin.Contains(model.Role))
        {
            ModelState.AddModelError(nameof(model.Role), "บทบาทไม่ถูกต้อง");
            return View(model);
        }

        var user = new IdentityUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await _userManager.AddToRoleAsync(user, model.Role);
        TempData["Success"] = $"สร้างผู้ใช้ {model.Email} สำเร็จ";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var currentRole = roles.FirstOrDefault() ?? AppRoles.Staff;
        var isSuperAdmin = roles.Contains(AppRoles.SuperAdmin);

        ViewBag.Roles = isSuperAdmin
            ? new SelectList(AppRoles.All, currentRole)
            : GetAssignableRoles(currentRole);

        return View(new EditUserViewModel
        {
            Id = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty,
            Role = currentRole,
            IsLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        var user = await _userManager.FindByIdAsync(model.Id);
        if (user == null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var isSuperAdmin = roles.Contains(AppRoles.SuperAdmin);
        ViewBag.Roles = isSuperAdmin
            ? new SelectList(AppRoles.All, model.Role)
            : GetAssignableRoles(model.Role);

        if (!ModelState.IsValid)
        {
            model.Email = user.Email ?? user.UserName ?? string.Empty;
            return View(model);
        }

        if (isSuperAdmin)
        {
            if (!AppRoles.All.Contains(model.Role))
            {
                ModelState.AddModelError(nameof(model.Role), "บทบาทไม่ถูกต้อง");
                return View(model);
            }
        }
        else if (!AppRoles.AssignableBySuperAdmin.Contains(model.Role))
        {
            ModelState.AddModelError(nameof(model.Role), "บทบาทไม่ถูกต้อง");
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!passwordResult.Succeeded)
            {
                foreach (var error in passwordResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                model.Email = user.Email ?? user.UserName ?? string.Empty;
                return View(model);
            }
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.FirstOrDefault() != model.Role)
        {
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, model.Role);
        }

        if (model.IsLocked)
        {
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        }
        else
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
        }

        TempData["Success"] = $"บันทึกข้อมูล {model.Email} สำเร็จ";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.Id == id)
        {
            TempData["Error"] = "ไม่สามารถลบบัญชีของตัวเองได้";
            return RedirectToAction(nameof(Index));
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains(AppRoles.SuperAdmin))
        {
            var superAdminCount = 0;
            foreach (var u in await _userManager.GetUsersInRoleAsync(AppRoles.SuperAdmin))
            {
                superAdminCount++;
            }

            if (superAdminCount <= 1)
            {
                TempData["Error"] = "ไม่สามารถลบ SuperAdmin คนสุดท้ายได้";
                return RedirectToAction(nameof(Index));
            }
        }

        var email = user.Email ?? user.UserName;
        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = $"ลบผู้ใช้ {email} สำเร็จ";
        return RedirectToAction(nameof(Index));
    }

    private SelectList GetAssignableRoles(string? selected = null)
    {
        return new SelectList(AppRoles.AssignableBySuperAdmin, selected);
    }
}
