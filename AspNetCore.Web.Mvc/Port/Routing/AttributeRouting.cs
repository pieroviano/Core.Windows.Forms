//
// MapMvcAttributeRoutes: turn every [Route] in the application into a System.Web.Routing.Route.
//
// The whole design in one sentence: an attribute route becomes an ordinary Route whose defaults pin
// controller and action, so everything downstream - action selection, model binding, filters, link
// generation - is the stock MVC pipeline and behaves exactly as a conventionally mapped route does.
//
// Ordering is the part that has to be got right, because it is what makes /orders/new reach New ()
// rather than Details (id: "new"). Routes are sorted by explicit Order, then by how SPECIFIC the
// template is: a literal segment beats a constrained parameter, which beats a plain parameter, which
// beats a catch-all. That is MVC 5's precedence rule, and applications are written assuming it.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Compilation;
using System.Web.Routing;

namespace System.Web.Mvc.Routing
{
	public static class AttributeRouting
	{
		/// <summary>
		/// Scans the application's controllers and maps every <see cref="RouteAttribute"/> found.
		/// Call from <c>RouteConfig</c> BEFORE your conventional routes.
		/// </summary>
		/// <remarks>
		/// Before, not after: routes match in order, so a conventional <c>{controller}/{action}/{id}</c>
		/// registered first would swallow every attribute route behind it. MVC 5 enforces this by
		/// keeping attribute routes in a separate collection; here they are ordinary routes, so the
		/// call order is the ordering.
		/// </remarks>
		public static void MapMvcAttributeRoutes (this RouteCollection routes)
		{
			MapMvcAttributeRoutes (routes, GetControllerTypes ());
		}

		/// <summary>
		/// Maps attribute routes for an explicit set of controller types. Useful in tests, and when
		/// the application's controllers are not discoverable by assembly scanning.
		/// </summary>
		public static void MapMvcAttributeRoutes (this RouteCollection routes, IEnumerable<Type> controllerTypes)
		{
			if (routes == null)
				throw new ArgumentNullException (nameof (routes));
			if (controllerTypes == null)
				throw new ArgumentNullException (nameof (controllerTypes));

			var entries = new List<Entry> ();
			foreach (Type controller in controllerTypes)
				entries.AddRange (BuildEntriesFor (controller));

			// OrderBy is a STABLE sort, so two equally specific routes keep declaration order rather
			// than swapping between runs on a hash-order change - which would be a maddening bug.
			// ThenBy (verb-constrained first): two actions can share one template and differ only by
			// verb - [HttpPost] Update and Details both on "catalog/{id:int}" is the ordinary REST
			// shape. They have identical precedence, so a stable sort would keep declaration order and
			// let whichever came first answer BOTH verbs. Constrained-before-unconstrained is the only
			// ordering that makes the pair work regardless of how they were written.
			foreach (Entry entry in entries
					.OrderBy (e => e.Order)
					.ThenBy (e => e.Precedence, PrecedenceComparer.Instance)
					.ThenBy (e => e.HasVerbConstraint ? 0 : 1)) {
				if (entry.Name != null) {
					if (routes [entry.Name] != null)
						throw new InvalidOperationException (
							"Two routes are both named '" + entry.Name + "'. Route names have to be unique " +
							"across the application - Url.RouteUrl would otherwise resolve one of them " +
							"arbitrarily.");

					routes.Add (entry.Name, entry.Route);
				} else {
					routes.Add (entry.Route);
				}
			}
		}

		static IEnumerable<Entry> BuildEntriesFor (Type controller)
		{
			if (controller == null || controller.IsAbstract || !typeof (IController).IsAssignableFrom (controller))
				yield break;

			string controllerName = controller.Name.EndsWith ("Controller", StringComparison.OrdinalIgnoreCase)
				? controller.Name.Substring (0, controller.Name.Length - "Controller".Length)
				: controller.Name;

			var prefixAttribute = controller.GetCustomAttribute<RoutePrefixAttribute> (inherit: true);
			string prefix = prefixAttribute != null ? prefixAttribute.Prefix : null;

			// A [Route] on the CONTROLLER is a fallback template for actions that have none of their
			// own - the "[Route("{action}")]" shape. It is not itself a route.
			var controllerRoutes = controller.GetCustomAttributes<RouteAttribute> (inherit: true).ToArray ();

			foreach (MethodInfo method in controller.GetMethods (BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
				if (method.IsSpecialName || method.GetCustomAttribute<NonActionAttribute> () != null)
					continue;

				var actionRoutes = method.GetCustomAttributes<RouteAttribute> (inherit: true).ToArray ();
				RouteAttribute [] applicable = actionRoutes.Length > 0 ? actionRoutes : controllerRoutes;
				if (applicable.Length == 0)
					continue;

				var actionNameAttribute = method.GetCustomAttribute<ActionNameAttribute> ();
				string actionName = actionNameAttribute != null ? actionNameAttribute.Name : method.Name;

				string [] verbs = GetVerbs (method);

				foreach (RouteAttribute attribute in applicable) {
					yield return BuildEntry (attribute, prefix, controllerName, actionName,
								 controller.Namespace, verbs);
				}
			}
		}

		static Entry BuildEntry (RouteAttribute attribute, string prefix, string controllerName,
					 string actionName, string controllerNamespace, string [] verbs)
		{
			string template = attribute.Template ?? String.Empty;

			// "~/" escapes the controller's prefix entirely - the documented way for one action to
			// live outside its controller's URL space.
			bool absolute = template.StartsWith ("~/", StringComparison.Ordinal);
			if (absolute)
				template = template.Substring (2);

			string full = absolute || String.IsNullOrEmpty (prefix)
				? template.Trim ('/')
				: (prefix + "/" + template.Trim ('/')).TrimEnd ('/');

			InlineRouteTemplate parsed = InlineRouteTemplate.Parse (full);

			var defaults = new RouteValueDictionary (parsed.Defaults) {
				["controller"] = controllerName,
				["action"] = actionName,
			};

			var constraints = new RouteValueDictionary (parsed.Constraints);
			if (verbs != null && verbs.Length > 0)
				constraints ["httpMethod"] = new HttpMethodConstraint (verbs);

			var route = new Route (parsed.Template, new MvcRouteHandler ()) {
				Defaults = defaults,
				Constraints = constraints,
				DataTokens = new RouteValueDictionary {
					// Read back by RouteAttribute.IsValidForRequest: an attribute-routed action is only
					// selectable for a request that arrived through one of these routes, never through
					// the conventional {controller}/{action}/{id}.
					[RouteAttribute.DataTokenKey] = true,
				},
			};

			// Namespace pinning, exactly as MapRoute's namespaces parameter does it: without this, two
			// controllers with the same name in different namespaces are an ambiguous-match exception
			// at request time rather than a resolved route.
			if (!String.IsNullOrEmpty (controllerNamespace)) {
				route.DataTokens ["Namespaces"] = new [] { controllerNamespace };
				route.DataTokens ["UseNamespaceFallback"] = false;
			}

			return new Entry {
				Route = route,
				Name = String.IsNullOrEmpty (attribute.Name) ? null : attribute.Name,
				Order = attribute.Order,
				Precedence = ComputePrecedence (full, parsed),
				HasVerbConstraint = verbs != null && verbs.Length > 0,
			};
		}

		/// <summary>
		/// Per-segment specificity, lower being more specific. Compared segment by segment, so
		/// "orders/new" sorts ahead of "orders/{id}" ahead of "orders/{*rest}".
		/// </summary>
		static int [] ComputePrecedence (string template, InlineRouteTemplate parsed)
		{
			var scores = new List<int> ();

			foreach (string segment in template.Split (new [] { '/' }, StringSplitOptions.RemoveEmptyEntries)) {
				if (segment.IndexOf ('{') < 0) {
					scores.Add (1);                                   // literal
					continue;
				}

				string body = segment.Trim ('{', '}');
				if (body.StartsWith ("*", StringComparison.Ordinal)) {
					scores.Add (5);                                   // catch-all
					continue;
				}

				string name = body.Split (':', '=', '?') [0].Trim ();
				bool optional = parsed.Defaults.ContainsKey (name);
				bool constrained = parsed.Constraints.ContainsKey (name);

				scores.Add (optional ? 4 : constrained ? 2 : 3);
			}

			return scores.ToArray ();
		}

		static string [] GetVerbs (MethodInfo method)
		{
			var verbs = new List<string> ();

			foreach (Attribute attribute in method.GetCustomAttributes (inherit: true).OfType<Attribute> ()) {
				var acceptVerbs = attribute as AcceptVerbsAttribute;
				if (acceptVerbs != null) {
					verbs.AddRange (acceptVerbs.Verbs);
					continue;
				}

				// HttpGetAttribute and friends wrap an AcceptVerbsAttribute in a private field, so the
				// verb is read off the attribute's own name instead. Brittle-looking, but the names are
				// part of MVC's public surface and cannot change.
				string name = attribute.GetType ().Name;
				if (name.StartsWith ("Http", StringComparison.Ordinal) &&
				    name.EndsWith ("Attribute", StringComparison.Ordinal)) {
					string verb = name.Substring (4, name.Length - 4 - "Attribute".Length);
					if (KnownVerbs.Contains (verb, StringComparer.OrdinalIgnoreCase))
						verbs.Add (verb.ToUpperInvariant ());
				}
			}

			return verbs.Distinct (StringComparer.OrdinalIgnoreCase).ToArray ();
		}

		static readonly string [] KnownVerbs = { "Get", "Post", "Put", "Delete", "Head", "Patch", "Options" };

		/// <summary>
		/// Every controller the application can see. Uses BuildManager, so App_Code and
		/// runtime-compiled controllers are included, not just what the SDK compiled.
		/// </summary>
		static IEnumerable<Type> GetControllerTypes ()
		{
			var types = new List<Type> ();

			foreach (Assembly assembly in BuildManager.GetReferencedAssemblies ().Cast<Assembly> ()) {
				if (assembly.IsDynamic)
					continue;

				Type [] candidates;
				try {
					candidates = assembly.GetTypes ();
				} catch (ReflectionTypeLoadException ex) {
					// One unloadable type in a referenced assembly must not take out route
					// registration for the whole application; take what did load.
					candidates = ex.Types.Where (t => t != null).ToArray ();
				} catch {
					continue;
				}

				foreach (Type type in candidates) {
					if (type != null && type.IsPublic && !type.IsAbstract &&
					    typeof (IController).IsAssignableFrom (type))
						types.Add (type);
				}
			}

			return types;
		}

		sealed class Entry
		{
			public Route Route;
			public string Name;
			public int Order;
			public int [] Precedence;
			public bool HasVerbConstraint;
		}

		sealed class PrecedenceComparer : IComparer<int []>
		{
			public static readonly PrecedenceComparer Instance = new PrecedenceComparer ();

			public int Compare (int [] x, int [] y)
			{
				int shared = Math.Min (x.Length, y.Length);
				for (int i = 0; i < shared; i++) {
					if (x [i] != y [i])
						return x [i].CompareTo (y [i]);
				}

				// Same prefix: the shorter template is the more specific one, because the longer one
				// has extra segments the shorter never has to match.
				return x.Length.CompareTo (y.Length);
			}
		}
	}
}
