//
// A controller whose VIEW does the awaiting, which is the case @await exists for.
//

using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Mvc.Routing;

namespace MvcSample.Controllers
{
	public class AsyncViewController : Controller
	{
		[Route ("async-view")]
		public ActionResult Index ()
		{
			return View ();
		}
	}

	/// <summary>
	/// Something for the view to await. Deliberately a real Task rather than Task.FromResult, so the
	/// continuation genuinely runs after a yield and a broken async transform cannot pass by accident.
	/// </summary>
	public static class SlowData
	{
		public static async Task<string> LoadAsync (string what)
		{
			await Task.Yield ();
			await Task.Delay (1);
			return "loaded:" + what;
		}

		public static async Task<int> CountAsync ()
		{
			await Task.Yield ();
			return 7;
		}
	}
}
