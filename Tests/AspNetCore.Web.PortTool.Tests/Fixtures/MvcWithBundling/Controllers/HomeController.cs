using System.Web.Mvc;

namespace LegacyMvc.Controllers
{
	public class HomeController : Controller
	{
		public ActionResult Index ()
		{
			return View ();
		}
	}
}
