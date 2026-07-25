//
// [Route] and [RoutePrefix].
//
// These are an MVC 5 feature and MVC 5 is not in the tree - upstream's aspnetwebstack submodule stops
// at MVC 4. They are written here rather than backported because MVC 5's implementation is entangled
// with a routing model this port does not have (RouteCollectionRoute, direct-route data tokens, a
// second action-selection pass). What applications actually depend on is the ATTRIBUTES and the
// URL behaviour, and both can be had by translating each attribute into an ordinary System.Web.Routing
// Route at startup - which is what AttributeRouting does.
//
// The consequence of that choice is recorded honestly in LIMITATIONS.md: everything downstream of the
// route table - action selection, model binding, filters, link generation - is the stock MVC pipeline,
// so it behaves exactly as a conventionally-mapped route would. What you do not get is MVC 5's
// internal notion of a "direct route", which nothing outside MVC 5 itself observes.
//

using System;

namespace System.Web.Mvc.Routing
{
	/// <summary>
	/// Maps a URL template directly onto an action, without a conventional route.
	/// </summary>
	/// <example>
	/// <code>
	/// [RoutePrefix ("orders")]
	/// public class OrdersController : Controller
	/// {
	///     [Route ("{id:int}")]              // matches /orders/42
	///     public ActionResult Details (int id) { ... }
	/// }
	/// </code>
	/// </example>
	/// <remarks>
	/// It is an <see cref="ActionMethodSelectorAttribute"/> as well as a marker, which is how the MVC 5
	/// rule "a controller with attribute routes is no longer reachable through conventional routes" is
	/// enforced here. Without it, <c>{controller}/{action}/{id}</c> still reaches these actions and
	/// quietly bypasses every constraint the attribute declared: <c>/catalog/search/ab</c> would run
	/// Search despite <c>minlength(3)</c>, because it arrived as controller=catalog, action=search.
	///
	/// MVC 5 achieves this by keeping attribute routes in a separate collection and excluding those
	/// controllers from conventional matching. Here the route table is shared, so the exclusion moves to
	/// action selection: the action is only valid for a request that actually arrived through one of
	/// these routes, which is what the data token records.
	/// </remarks>
	[AttributeUsage (AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
	public sealed class RouteAttribute : ActionMethodSelectorAttribute
	{
		/// <summary>Data token every attribute route carries, so its actions can recognise it.</summary>
		internal const string DataTokenKey = "__attributeRoute";

		public override bool IsValidForRequest (ControllerContext controllerContext, System.Reflection.MethodInfo methodInfo)
		{
			if (controllerContext == null || controllerContext.RouteData == null)
				return false;

			return controllerContext.RouteData.DataTokens.ContainsKey (DataTokenKey);
		}

		public RouteAttribute ()
			: this (String.Empty)
		{
		}

		public RouteAttribute (string template)
		{
			// Empty is meaningful, not missing: [Route ("")] on an action under [RoutePrefix ("orders")]
			// is how you say "this action IS /orders". Null is the error.
			Template = template ?? throw new ArgumentNullException (nameof (template));
		}

		/// <summary>
		/// The URL template, relative to any <see cref="RoutePrefixAttribute"/> on the controller.
		/// A leading <c>~/</c> escapes the prefix and makes the template absolute.
		/// </summary>
		public string Template { get; }

		/// <summary>
		/// Route name, for <c>Url.RouteUrl</c> and <c>Html.RouteLink</c>. Must be unique across the
		/// application; a duplicate is an error at startup rather than a silently ignored route.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Explicit ordering. Lower runs first; the default is 0. Within one order, routes are sorted
		/// by how specific their template is - literal segments beat constrained parameters, which
		/// beat plain parameters, which beat catch-alls.
		/// </summary>
		public int Order { get; set; }
	}

	/// <summary>
	/// A common URL prefix for every <see cref="RouteAttribute"/> on a controller.
	/// </summary>
	[AttributeUsage (AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
	public sealed class RoutePrefixAttribute : Attribute
	{
		public RoutePrefixAttribute (string prefix)
		{
			if (prefix == null)
				throw new ArgumentNullException (nameof (prefix));

			Prefix = prefix.Trim ('/');
		}

		public string Prefix { get; }
	}
}
