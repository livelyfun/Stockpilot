using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockPilot.Web.Models;
using StockPilot.Web.Services;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IDashboardService _dashboard;

    public HomeController(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var fromDate = (from ?? today.AddDays(-29)).Date;
        var toDate = (to ?? today).Date;

        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        if ((toDate - fromDate).TotalDays > 366)
        {
            fromDate = toDate.AddDays(-366);
        }

        try
        {
            var model = await _dashboard.GetAsync(new DashboardFilter(fromDate, toDate), cancellationToken);
            return View(model);
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
            var model = await _dashboard.GetAsync(new DashboardFilter(today.AddDays(-29), today), cancellationToken);
            return View(model);
        }
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [AllowAnonymous]
    [Route("Home/StatusCode/{code:int}")]
    public IActionResult HttpStatusCode(int code)
    {
        ViewData["StatusCode"] = code;
        ViewData["Title"] = code switch
        {
            403 => "Access denied",
            404 => "Page not found",
            400 => "Invalid request",
            _ => "Something went wrong"
        };
        return View("StatusCode");
    }
}
