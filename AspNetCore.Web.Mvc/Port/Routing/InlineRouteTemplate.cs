//
// Splitting "orders/{id:int?}" into the three things System.Web.Routing.Route actually takes:
// a plain template, a defaults dictionary and a constraints dictionary.
//
// System.Web.Routing's own parser predates inline syntax entirely - it understands "{id}" and
// "{*rest}" and nothing else. Handing it "{id:int}" does not fail; it creates a parameter genuinely
// NAMED "id:int", which then never binds to anything and produces an action whose id is always null.
// That silent version is the reason this runs before the Route is constructed rather than trying to
// teach the parser new syntax.
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Routing;

namespace System.Web.Mvc.Routing
{
	/// <summary>
	/// A route template with its inline constraints and defaults lifted out.
	/// </summary>
	public sealed class InlineRouteTemplate
	{
		InlineRouteTemplate (string template, RouteValueDictionary defaults,
				     RouteValueDictionary constraints, IList<string> parameters)
		{
			Template = template;
			Defaults = defaults;
			Constraints = constraints;
			ParameterNames = parameters;
		}

		/// <summary>The template with all <c>:constraint</c>, <c>?</c> and <c>=default</c> removed.</summary>
		public string Template { get; }

		public RouteValueDictionary Defaults { get; }

		public RouteValueDictionary Constraints { get; }

		/// <summary>Parameter names in template order, used to compute route precedence.</summary>
		public IList<string> ParameterNames { get; }

		/// <summary>
		/// Parses <c>{name}</c>, <c>{name?}</c>, <c>{name=default}</c>, <c>{name:constraint}</c>,
		/// <c>{name:constraint(args)}</c>, <c>{*catchAll}</c> and any combination.
		/// </summary>
		public static InlineRouteTemplate Parse (string template)
		{
			if (template == null)
				throw new ArgumentNullException (nameof (template));

			var clean = new StringBuilder (template.Length);
			var defaults = new RouteValueDictionary ();
			var constraints = new RouteValueDictionary ();
			var parameters = new List<string> ();

			int i = 0;
			while (i < template.Length) {
				char c = template [i];
				if (c != '{') {
					clean.Append (c);
					i++;
					continue;
				}

				int close = FindClosingBrace (template, i);
				if (close < 0)
					throw new InvalidOperationException (
						"The route template '" + template + "' has an unclosed '{'.");

				string body = template.Substring (i + 1, close - i - 1);
				i = close + 1;

				bool catchAll = body.StartsWith ("*", StringComparison.Ordinal);
				if (catchAll)
					body = body.Substring (1);

				// Order matters: the default value is stripped first, because "{id=a:b}" means the
				// literal default "a:b", not a constraint. Splitting on ':' first would corrupt it.
				string defaultValue = null;
				int equals = body.IndexOf ('=');
				if (equals >= 0) {
					defaultValue = body.Substring (equals + 1);
					body = body.Substring (0, equals);
				}

				var specifications = new List<string> ();
				string name = SplitConstraints (body, specifications);

				bool optional = name.EndsWith ("?", StringComparison.Ordinal);
				if (optional)
					name = name.Substring (0, name.Length - 1);

				name = name.Trim ();
				if (name.Length == 0)
					throw new InvalidOperationException (
						"The route template '" + template + "' has a parameter with no name.");

				parameters.Add (name);
				clean.Append ('{').Append (catchAll ? "*" : "").Append (name).Append ('}');

				if (defaultValue != null)
					defaults [name] = defaultValue;
				else if (optional)
					// UrlParameter.Optional, not null: a null default makes the parameter REQUIRED to
					// be absent-or-null in generated URLs, which breaks Url.Action round-tripping.
					defaults [name] = UrlParameter.Optional;

				if (specifications.Count > 0)
					constraints [name] = Combine (specifications);
			}

			return new InlineRouteTemplate (clean.ToString (), defaults, constraints, parameters);
		}

		/// <summary>
		/// Splits "id:min(1):max(9)" into the name and each constraint, respecting parentheses so a
		/// ':' inside regex(...) is not treated as a separator.
		/// </summary>
		static string SplitConstraints (string body, IList<string> specifications)
		{
			int depth = 0, start = -1;
			var name = new StringBuilder ();

			for (int i = 0; i < body.Length; i++) {
				char c = body [i];

				if (c == '(')
					depth++;
				else if (c == ')')
					depth--;

				if (c == ':' && depth == 0) {
					if (start >= 0)
						specifications.Add (body.Substring (start, i - start));
					else
						name.Append (body.Substring (0, i));

					start = i + 1;
					continue;
				}
			}

			if (start >= 0)
				specifications.Add (body.Substring (start));
			else
				name.Append (body);

			return name.ToString ();
		}

		static object Combine (IList<string> specifications)
		{
			if (specifications.Count == 1)
				return InlineRouteConstraintResolver.Resolve (specifications [0]);

			var all = new List<IRouteConstraint> ();
			foreach (string specification in specifications)
				all.Add (InlineRouteConstraintResolver.Resolve (specification));

			return new CompoundRouteConstraint (all);
		}

		static int FindClosingBrace (string template, int open)
		{
			int depth = 0;
			for (int i = open; i < template.Length; i++) {
				if (template [i] == '{')
					depth++;
				else if (template [i] == '}') {
					depth--;
					if (depth == 0)
						return i;
				}
			}

			return -1;
		}

		/// <summary>All of several constraints on one parameter, e.g. <c>{n:int:min(1)}</c>.</summary>
		sealed class CompoundRouteConstraint : IRouteConstraint
		{
			readonly IList<IRouteConstraint> constraints;

			public CompoundRouteConstraint (IList<IRouteConstraint> constraints)
			{
				this.constraints = constraints;
			}

			public bool Match (HttpContextBase httpContext, Route route, string parameterName,
					   RouteValueDictionary values, RouteDirection routeDirection)
			{
				foreach (IRouteConstraint constraint in constraints) {
					if (!constraint.Match (httpContext, route, parameterName, values, routeDirection))
						return false;
				}

				return true;
			}
		}
	}
}
