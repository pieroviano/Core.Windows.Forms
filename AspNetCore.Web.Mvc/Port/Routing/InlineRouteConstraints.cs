//
// The inline constraint vocabulary - the ":int" in "{id:int}".
//
// System.Web.Routing has IRouteConstraint and exactly one implementation (HttpMethodConstraint);
// everything else was always supplied by the framework above it. MVC 5 and Web API 2 each shipped
// their own set, so this port needs one too.
//
// The names and semantics here are MVC 5's, deliberately: an application being ported has these
// strings written into its source, and a constraint that silently means something slightly different
// is worse than one that is missing - a missing name throws at startup, naming the template.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web.Routing;

namespace System.Web.Mvc.Routing
{
	/// <summary>
	/// Base for constraints that only have to look at one value and say yes or no.
	/// </summary>
	public abstract class InlineRouteConstraint : IRouteConstraint
	{
		public bool Match (HttpContextBase httpContext, Route route, string parameterName,
				   RouteValueDictionary values, RouteDirection routeDirection)
		{
			if (values == null || parameterName == null)
				return false;

			object value;
			if (!values.TryGetValue (parameterName, out value) || value == null)
				// An absent value is not a failed constraint. The parameter is either optional - in
				// which case the route matches without it - or required, in which case the template
				// itself has already rejected the URL.
				return true;

			if (value is UrlParameter)
				return true;

			string text = Convert.ToString (value, CultureInfo.InvariantCulture);
			if (text.Length == 0)
				return true;

			return IsMatch (text, value);
		}

		protected abstract bool IsMatch (string text, object value);
	}

	sealed class TypeRouteConstraint : InlineRouteConstraint
	{
		readonly Func<string, bool> parses;

		public TypeRouteConstraint (Func<string, bool> parses)
		{
			this.parses = parses;
		}

		protected override bool IsMatch (string text, object value)
		{
			return parses (text);
		}
	}

	sealed class RegexRouteConstraint : InlineRouteConstraint
	{
		readonly Regex pattern;

		public RegexRouteConstraint (string pattern)
		{
			// Anchored, because "{id:regex(\\d+)}" plainly means the whole segment is digits. An
			// unanchored regex would match "12abc" and hand the action a value it cannot parse.
			this.pattern = new Regex ("^(?:" + pattern + ")$",
						  RegexOptions.CultureInvariant | RegexOptions.Compiled);
		}

		protected override bool IsMatch (string text, object value)
		{
			return pattern.IsMatch (text);
		}
	}

	sealed class LengthRouteConstraint : InlineRouteConstraint
	{
		readonly int min, max;

		public LengthRouteConstraint (int min, int max)
		{
			this.min = min;
			this.max = max;
		}

		protected override bool IsMatch (string text, object value)
		{
			return text.Length >= min && text.Length <= max;
		}
	}

	sealed class RangeRouteConstraint : InlineRouteConstraint
	{
		readonly long min, max;

		public RangeRouteConstraint (long min, long max)
		{
			this.min = min;
			this.max = max;
		}

		protected override bool IsMatch (string text, object value)
		{
			long parsed;
			if (!Int64.TryParse (text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
				return false;

			return parsed >= min && parsed <= max;
		}
	}

	/// <summary>
	/// Turns a constraint name and its arguments into an <see cref="IRouteConstraint"/>.
	/// </summary>
	public static class InlineRouteConstraintResolver
	{
		/// <summary>
		/// Register your own, or replace one of the built-ins. The key is the name as written in a
		/// template; the argument is whatever was in the parentheses, or null.
		/// </summary>
		public static readonly IDictionary<string, Func<string [], IRouteConstraint>> Constraints =
			new Dictionary<string, Func<string [], IRouteConstraint>> (StringComparer.OrdinalIgnoreCase) {
				["int"] = _ => Parses (s => { int v; return Int32.TryParse (s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v); }),
				["long"] = _ => Parses (s => { long v; return Int64.TryParse (s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v); }),
				["bool"] = _ => Parses (s => { bool v; return Boolean.TryParse (s, out v); }),
				["guid"] = _ => Parses (s => { Guid v; return Guid.TryParse (s, out v); }),
				["decimal"] = _ => Parses (s => { decimal v; return Decimal.TryParse (s, NumberStyles.Number, CultureInfo.InvariantCulture, out v); }),
				["double"] = _ => Parses (s => { double v; return Double.TryParse (s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v); }),
				["float"] = _ => Parses (s => { float v; return Single.TryParse (s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v); }),
				["datetime"] = _ => Parses (s => { DateTime v; return DateTime.TryParse (s, CultureInfo.InvariantCulture, DateTimeStyles.None, out v); }),
				["alpha"] = _ => new RegexRouteConstraint ("[A-Za-z]*"),
				["required"] = _ => Parses (s => s.Length > 0),
				["regex"] = a => new RegexRouteConstraint (Argument (a, 0, "regex")),
				["length"] = a => a.Length == 1
					? new LengthRouteConstraint (Number (a, 0, "length"), Number (a, 0, "length"))
					: new LengthRouteConstraint (Number (a, 0, "length"), Number (a, 1, "length")),
				["minlength"] = a => new LengthRouteConstraint (Number (a, 0, "minlength"), Int32.MaxValue),
				["maxlength"] = a => new LengthRouteConstraint (0, Number (a, 0, "maxlength")),
				["min"] = a => new RangeRouteConstraint (Number (a, 0, "min"), Int64.MaxValue),
				["max"] = a => new RangeRouteConstraint (Int64.MinValue, Number (a, 0, "max")),
				["range"] = a => new RangeRouteConstraint (Number (a, 0, "range"), Number (a, 1, "range")),
			};

		static IRouteConstraint Parses (Func<string, bool> test)
		{
			return new TypeRouteConstraint (test);
		}

		static string Argument (string [] args, int index, string name)
		{
			if (args == null || args.Length <= index)
				throw new InvalidOperationException (
					"The inline route constraint '" + name + "' needs an argument, e.g. {id:" + name + "(...)}.");

			return args [index];
		}

		static int Number (string [] args, int index, string name)
		{
			int value;
			if (!Int32.TryParse (Argument (args, index, name), NumberStyles.Integer,
					     CultureInfo.InvariantCulture, out value))
				throw new InvalidOperationException (
					"The inline route constraint '" + name + "' needs whole-number arguments.");

			return value;
		}

		/// <summary>
		/// Resolves one constraint, e.g. <c>int</c> or <c>range(1,10)</c>.
		/// </summary>
		public static IRouteConstraint Resolve (string specification)
		{
			if (String.IsNullOrEmpty (specification))
				return null;

			string name = specification;
			string [] args = null;

			int open = specification.IndexOf ('(');
			if (open >= 0 && specification.EndsWith (")", StringComparison.Ordinal)) {
				name = specification.Substring (0, open);
				string body = specification.Substring (open + 1, specification.Length - open - 2);

				// regex() takes ONE argument that may itself contain commas - "{n:regex(a{2,3})}" is
				// legal and splitting it would silently produce two nonsense arguments.
				args = String.Equals (name, "regex", StringComparison.OrdinalIgnoreCase)
					? new [] { body }
					: body.Split (',');

				for (int i = 0; i < args.Length; i++)
					args [i] = args [i].Trim ();
			}

			Func<string [], IRouteConstraint> factory;
			if (!Constraints.TryGetValue (name, out factory))
				throw new InvalidOperationException (
					"Unknown inline route constraint '" + name + "'. Known constraints are: " +
					String.Join (", ", new List<string> (Constraints.Keys).ToArray ()) +
					". Add your own to InlineRouteConstraintResolver.Constraints.");

			return factory (args);
		}
	}
}
