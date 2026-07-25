//
// The stock App_Start/BundleConfig.cs shape, unchanged from what an ASP.NET MVC application has.
//
// That is the whole claim being made: this file is what the MVC project template generated, and it
// compiles and runs here as written.
//

using System.Web.Optimization;

namespace MvcSample
{
	public class BundleConfig
	{
		public static void RegisterBundles (BundleCollection bundles)
		{
			bundles.Add (new ScriptBundle ("~/bundles/app")
				.Include ("~/Scripts/first.js", "~/Scripts/second.js"));

			bundles.Add (new StyleBundle ("~/Content/css")
				.Include ("~/Content/site.css", "~/Content/print.css"));

			// A wildcard include, which is how the template's jquery bundle is written
			// ("~/Scripts/jquery-{version}.js") and the reason patterns are supported at all.
			bundles.Add (new ScriptBundle ("~/bundles/all-scripts")
				.Include ("~/Scripts/*.js"));

			// Off by default in a debug build, so views render individual files. Forced on here so the
			// sample demonstrates real bundling regardless of how it was built.
			BundleTable.EnableOptimizations = true;
		}
	}
}
