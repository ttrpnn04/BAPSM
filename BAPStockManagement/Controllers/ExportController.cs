using BAPStockManagement.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BAPStockManagement.Controllers;

[Authorize(Roles = AppRoles.StockView)]
public class ExportController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
