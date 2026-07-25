//
// Serving a bundle: the route handler and the IHttpHandler behind it.
//

using System;
using System.Text;
using System.Web;
using System.Web.Routing;

namespace System.Web.Optimization
{
	/// <summary>
	/// A bundle's route. Matches incoming requests and refuses to generate outgoing URLs.
	/// </summary>
	/// <remarks>
	/// The refusal is the whole reason this class exists. A bundle template is all literal - "bundles/app"
	/// - and System.Web.Routing treats a literal-only route as able to generate a URL for ANY set of
	/// values, putting the ones it does not recognise in the query string. Since bundle routes sit at the
	/// front of the table, Html.ActionLink ("Details", "Home", new { id = 1 }) would otherwise return
	/// "/bundles/all-scripts?action=Details&amp;controller=Home&amp;id=1" - a broken link on every page in
	/// the application, from adding a bundle.
	/// </remarks>
	sealed class BundleRoute : Route
	{
		public BundleRoute (string url, IRouteHandler routeHandler)
			: base (url, routeHandler)
		{
		}

		public override VirtualPathData GetVirtualPath (RequestContext requestContext, RouteValueDictionary values)
		{
			return null;
		}
	}

	sealed class BundleRouteHandler : IRouteHandler
	{
		readonly BundleCollection collection;
		readonly Bundle bundle;

		public BundleRouteHandler (BundleCollection collection, Bundle bundle)
		{
			this.collection = collection;
			this.bundle = bundle;
		}

		public IHttpHandler GetHttpHandler (RequestContext requestContext)
		{
			return new BundleHandler (collection, bundle);
		}
	}

	sealed class BundleHandler : IHttpHandler
	{
		readonly BundleCollection collection;
		readonly Bundle bundle;

		public BundleHandler (BundleCollection collection, Bundle bundle)
		{
			this.collection = collection;
			this.bundle = bundle;
		}

		public bool IsReusable {
			get { return false; }
		}

		public void ProcessRequest (HttpContext context)
		{
			var wrapper = new HttpContextWrapper (context);
			var bundleContext = new BundleContext (wrapper, collection, bundle.Path) {
				EnableOptimizations = BundleTable.EnableOptimizations,
			};

			BundleResponse response = bundle.GenerateBundleResponse (bundleContext);

			context.Response.ContentType = response.ContentType ?? ContentTypeFor (bundle);
			context.Response.ContentEncoding = Encoding.UTF8;

			// Cached hard and keyed by the ?v= hash in the URL. Without the hash a year-long cache
			// would be reckless; with it, a changed file changes the URL, so it is exactly right.
			bool versioned = !String.IsNullOrEmpty (context.Request.QueryString ["v"]);
			HttpCachePolicy cache = context.Response.Cache;
			cache.SetCacheability (versioned ? HttpCacheability.Public : HttpCacheability.NoCache);
			if (versioned) {
				cache.SetExpires (DateTime.UtcNow.AddYears (1));
				cache.SetMaxAge (TimeSpan.FromDays (365));
				cache.SetValidUntilExpires (true);
			}

			context.Response.Write (response.Content);
		}

		static string ContentTypeFor (Bundle bundle)
		{
			return bundle is StyleBundle ? "text/css" : "text/javascript";
		}
	}
}
