using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DevPath.Web.Models;

namespace DevPath.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    // Endpoint do exemplo HTMX da Home: devolve só a partial, sem o layout.
    [HttpGet]
    public IActionResult ServerTime()
    {
        return PartialView("_ServerTime", new ServerTimeViewModel(DateTimeOffset.Now));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
