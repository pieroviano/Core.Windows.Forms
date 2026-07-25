//
// The inline template parser and constraint vocabulary, on their own.
//
// Worth testing directly rather than only through HTTP: the parser's job is to lift ":int" out of
// "{id:int}" BEFORE System.Web.Routing sees it, and getting that wrong is silent - the route is built
// with a parameter genuinely named "id:int", which matches nothing and binds nothing. There is no
// exception to catch, only an action whose argument is always null.
//

using System;
using System.Web.Mvc;
using System.Web.Mvc.Routing;
using System.Web.Routing;
using Xunit;

namespace WebFormsPort.MvcBrowserTests
{
	public class RouteTemplateTests
	{
		[Fact]
		public void A_constraint_is_stripped_from_the_template_and_kept_separately ()
		{
			InlineRouteTemplate parsed = InlineRouteTemplate.Parse ("orders/{id:int}");

			Assert.Equal ("orders/{id}", parsed.Template);
			Assert.True (parsed.Constraints.ContainsKey ("id"));
			Assert.False (parsed.Constraints.ContainsKey ("id:int"));
		}

		[Fact]
		public void An_optional_parameter_defaults_to_UrlParameter_Optional ()
		{
			// Not null: a null default makes the parameter required-to-be-absent when generating URLs,
			// which quietly breaks Url.Action round-tripping.
			InlineRouteTemplate parsed = InlineRouteTemplate.Parse ("orders/{id?}");

			Assert.Equal ("orders/{id}", parsed.Template);
			Assert.Same (UrlParameter.Optional, parsed.Defaults ["id"]);
		}

		[Fact]
		public void An_inline_default_is_kept_as_the_default_value ()
		{
			InlineRouteTemplate parsed = InlineRouteTemplate.Parse ("orders/{page=1}");

			Assert.Equal ("orders/{page}", parsed.Template);
			Assert.Equal ("1", parsed.Defaults ["page"]);
		}

		[Fact]
		public void A_default_containing_a_colon_is_not_mistaken_for_a_constraint ()
		{
			// "{when=12:30}" - the default is stripped first for exactly this reason.
			InlineRouteTemplate parsed = InlineRouteTemplate.Parse ("at/{when=12:30}");

			Assert.Equal ("at/{when}", parsed.Template);
			Assert.Equal ("12:30", parsed.Defaults ["when"]);
			Assert.False (parsed.Constraints.ContainsKey ("when"));
		}

		[Fact]
		public void A_catch_all_keeps_its_star ()
		{
			InlineRouteTemplate parsed = InlineRouteTemplate.Parse ("files/{*path}");

			Assert.Equal ("files/{*path}", parsed.Template);
		}

		[Fact]
		public void Several_constraints_can_apply_to_one_parameter ()
		{
			InlineRouteTemplate parsed = InlineRouteTemplate.Parse ("s/{term:alpha:minlength(3)}");

			Assert.Equal ("s/{term}", parsed.Template);
			Assert.True (parsed.Constraints.ContainsKey ("term"));
		}

		[Fact]
		public void A_regex_constraint_may_contain_commas_and_colons ()
		{
			// "{n:regex(a{2,3})}" is legal, and splitting its argument on ',' would produce nonsense.
			InlineRouteTemplate parsed = InlineRouteTemplate.Parse ("x/{n:regex(^a{2,3}$)}");

			Assert.Equal ("x/{n}", parsed.Template);
			Assert.True (parsed.Constraints.ContainsKey ("n"));
		}

		[Fact]
		public void An_unclosed_brace_is_an_error_rather_than_a_strange_parameter_name ()
		{
			Assert.Throws<InvalidOperationException> (() => InlineRouteTemplate.Parse ("orders/{id"));
		}

		[Fact]
		public void An_unknown_constraint_names_itself_and_the_alternatives ()
		{
			var ex = Assert.Throws<InvalidOperationException> (
				() => InlineRouteTemplate.Parse ("orders/{id:nosuchconstraint}"));

			Assert.Contains ("nosuchconstraint", ex.Message);
			Assert.Contains ("int", ex.Message);
		}

		[Theory]
		[InlineData ("int", "42", true)]
		[InlineData ("int", "4.2", false)]
		[InlineData ("int", "abc", false)]
		[InlineData ("bool", "true", true)]
		[InlineData ("bool", "yes", false)]
		[InlineData ("guid", "9f1b7a52-0e2a-4a0e-9f2f-2b6e0b1f1a11", true)]
		[InlineData ("guid", "nope", false)]
		[InlineData ("alpha", "widgets", true)]
		[InlineData ("alpha", "widget5", false)]
		[InlineData ("minlength(3)", "abc", true)]
		[InlineData ("minlength(3)", "ab", false)]
		[InlineData ("maxlength(3)", "abc", true)]
		[InlineData ("maxlength(3)", "abcd", false)]
		[InlineData ("length(2)", "ab", true)]
		[InlineData ("length(2)", "abc", false)]
		[InlineData ("range(1,10)", "5", true)]
		[InlineData ("range(1,10)", "11", false)]
		[InlineData ("min(5)", "5", true)]
		[InlineData ("min(5)", "4", false)]
		[InlineData ("max(5)", "5", true)]
		[InlineData ("max(5)", "6", false)]
		[InlineData ("regex(^a+$)", "aaa", true)]
		[InlineData ("regex(^a+$)", "aab", false)]
		public void Constraints_match_what_they_say (string specification, string value, bool expected)
		{
			IRouteConstraint constraint = InlineRouteConstraintResolver.Resolve (specification);
			var values = new RouteValueDictionary { ["v"] = value };

			Assert.Equal (expected,
				      constraint.Match (null, null, "v", values, RouteDirection.IncomingRequest));
		}

		[Fact]
		public void A_regex_constraint_is_anchored ()
		{
			// Unanchored, "{id:regex(\d+)}" would accept "12abc" and hand the action a value it cannot
			// parse - the constraint would look like it worked right up until the model binder.
			IRouteConstraint constraint = InlineRouteConstraintResolver.Resolve (@"regex(\d+)");
			var values = new RouteValueDictionary { ["v"] = "12abc" };

			Assert.False (constraint.Match (null, null, "v", values, RouteDirection.IncomingRequest));
		}

		[Fact]
		public void An_absent_value_does_not_fail_a_constraint ()
		{
			// An optional parameter that was not supplied has nothing to validate. Failing here would
			// make "{id:int?}" unmatchable without an id, which is the opposite of optional.
			IRouteConstraint constraint = InlineRouteConstraintResolver.Resolve ("int");

			Assert.True (constraint.Match (null, null, "v", new RouteValueDictionary (),
						       RouteDirection.IncomingRequest));
			Assert.True (constraint.Match (null, null, "v",
						       new RouteValueDictionary { ["v"] = UrlParameter.Optional },
						       RouteDirection.IncomingRequest));
		}
	}
}
