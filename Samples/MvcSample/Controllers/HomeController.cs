using System.Collections.Generic;
using System.Web.Mvc;

namespace MvcSample.Controllers
{
	public class Widget
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public decimal Price { get; set; }
	}

	// A plain MVC 3 controller. Nothing here is port-specific - this is the surface an existing
	// application already has.
	public class HomeController : Controller
	{
		static readonly List<Widget> widgets = new List<Widget> {
			new Widget { Id = 1, Name = "sprocket", Price = 9.99m },
			new Widget { Id = 2, Name = "flange",   Price = 24.50m },
			new Widget { Id = 3, Name = "grommet",  Price = 3.75m },
		};

		public ActionResult Index ()
		{
			ViewBag.Heading = "Widgets";
			return View (widgets);
		}

		public ActionResult Details (int id)
		{
			Widget widget = widgets.Find (w => w.Id == id);
			if (widget == null)
				return HttpNotFound ();

			return View (widget);
		}

		// Model binding from the query string / form, and a redirect result.
		[HttpPost]
		public ActionResult Echo (string message)
		{
			TempData ["echo"] = "you said: " + message;
			return RedirectToAction ("Index");
		}

		// Content and JSON results, which need no view at all.
		public ActionResult Plain ()
		{
			return Content ("plain content result", "text/plain");
		}

		public ActionResult Data ()
		{
			return Json (new { total = widgets.Count, first = widgets [0].Name },
				     JsonRequestBehavior.AllowGet);
		}

		// Renders @Scripts.Render / @Styles.Render against the bundles registered in BundleConfig.
		public ActionResult Bundles ()
		{
			return View ();
		}
	}
}
