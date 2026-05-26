namespace FiscalPlatform.API.Controllers;
public sealed class HomeController : Microsoft.AspNetCore.Mvc.Controller
{
    public Microsoft.AspNetCore.Mvc.IActionResult Index() => View();
}
