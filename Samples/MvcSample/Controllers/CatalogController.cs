//
// Attribute routing, and an async action.
//
// Nothing here configures a route: every URL this controller answers on is declared by the attribute
// directly above the action, and MapMvcAttributeRoutes turns each one into a real route at startup.
//

using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Mvc.Routing;

namespace MvcSample.Controllers
{
	[RoutePrefix ("catalog")]
	public class CatalogController : Controller
	{
		[Route ("")]
		public ActionResult Index ()
		{
			return Content ("catalog index");
		}

		// Deliberately declared AFTER Details, to prove ordering is by specificity rather than by
		// declaration: /catalog/new has to reach here, not Details with id "new".
		[Route ("new")]
		public ActionResult New ()
		{
			return Content ("catalog new");
		}

		[Route ("{id:int}")]
		public ActionResult Details (int id)
		{
			return Content ("catalog details " + id);
		}

		[Route ("{id:int}/reviews/{page:int=1}")]
		public ActionResult Reviews (int id, int page)
		{
			return Content ("catalog reviews " + id + " page " + page);
		}

		[Route ("search/{term:alpha:minlength(3)}")]
		public ActionResult Search (string term)
		{
			return Content ("catalog search " + term);
		}

		[Route ("named", Name = "catalog-named")]
		public ActionResult Named ()
		{
			// Round-trips the route name back into a URL, which is the reason names exist at all.
			return Content ("catalog named -> " + Url.RouteUrl ("catalog-named"));
		}

		[HttpPost]
		[Route ("{id:int}")]
		public ActionResult Update (int id)
		{
			return Content ("catalog update " + id);
		}

		[Route ("~/legacy-catalog")]
		public ActionResult Legacy ()
		{
			// "~/" escapes the [RoutePrefix] entirely.
			return Content ("catalog legacy");
		}

		[Route ("files/{*path}")]
		public ActionResult Files (string path)
		{
			return Content ("catalog files " + path);
		}

		/// <summary>An action that awaits, feeding the async view.</summary>
		[Route ("async")]
		public async Task<ActionResult> AsyncAction ()
		{
			await Task.Yield ();
			return Content ("catalog async");
		}
	}
}
