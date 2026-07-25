using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace LegacyMvc
{
	public class MvcApplication : HttpApplication
	{
		protected void Application_Start ()
		{
			RouteTable.Routes.MapRoute ("Default", "{controller}/{action}/{id}",
						    new { controller = "Home", action = "Index", id = UrlParameter.Optional });

			BundleConfig.RegisterBundles (BundleTable.Bundles);
		}
	}
}
