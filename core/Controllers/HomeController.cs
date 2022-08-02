using Microsoft.AspNetCore.Mvc;

namespace CypherNetwork.Controllers;

public class HomeController : Controller
{
    /// <summary>
    /// </summary>
    /// <returns></returns>
    public IActionResult Index()
    {
        return new RedirectResult("~/swagger");
    }
}