using DevPath.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevPath.Web.Controllers;

/// <summary>Área do aluno. [Authorize]: sem login, o cookie middleware redireciona para /Account/Login.</summary>
[Authorize]
public sealed class DashboardController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["DisplayName"] = User.FindFirst(DevPathClaimTypes.DisplayName)?.Value ?? User.Identity?.Name;
        return View();
    }
}
