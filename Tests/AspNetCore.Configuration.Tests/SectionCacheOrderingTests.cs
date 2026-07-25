//
// Pins down the order-dependence in WebConfigurationManager.GetSection (name, path).
//
// The section cache is process-global and one-shot per key, so the ONLY way to observe ordering is to
// perform the sequence inside a single test - two [Fact]s cannot do it, because whichever runs first
// warms the cache for the other.
//

using System.Configuration;
using System.Linq;
using System.Web.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace WebFormsPort.ConfigurationTests
{
	[Collection (ConfigurationCollection.Name)]
	public class SectionCacheOrderingTests
	{
		readonly ConfigurationFixture fixture;
		readonly ITestOutputHelper output;

		public SectionCacheOrderingTests (ConfigurationFixture fixture, ITestOutputHelper output)
		{
			this.fixture = fixture;
			this.output = output;
		}

		static bool DeniesAnonymous (string path)
		{
			var section = (AuthorizationSection) WebConfigurationManager.GetSection ("system.web/authorization", path);
			return section != null && section.Rules.Cast<AuthorizationRule> ()
				.Any (rule => rule.Action == AuthorizationRuleAction.Deny && rule.Users.Contains ("?"));
		}

		[Fact]
		public void Every_spelling_of_the_secure_directory_agrees_whatever_the_order ()
		{
			// All four name the same place, so all four must give the same answer - and the answer
			// must not depend on which was asked first.
			string [] paths = { "/Secure", "/Secure/", "~/Secure", "/Secure/Secret.aspx" };

			// Root FIRST, so its own cache entry exists before anything else is asked. If the root then
			// changes answer, the cache is not what is wrong - the shared configuration state is.
			output.WriteLine ($"{"/ (before)",-24} denies anonymous: {DeniesAnonymous ("/")}");

			foreach (string path in paths)
				output.WriteLine ($"{path,-24} denies anonymous: {DeniesAnonymous (path)}");

			output.WriteLine ($"{"/ (after)",-24} denies anonymous: {DeniesAnonymous ("/")}");

			foreach (string path in paths)
				Assert.True (DeniesAnonymous (path),
					     $"'{path}' did not see Secure/web.config's deny rule");

			// And the root must still be unaffected after all of that.
			Assert.False (DeniesAnonymous ("/"), "the Secure/ deny rule leaked to the application root");
		}
	}
}
