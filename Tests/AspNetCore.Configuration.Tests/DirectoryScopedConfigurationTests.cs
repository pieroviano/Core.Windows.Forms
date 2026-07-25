//
// Directory-scoped configuration: does a sub-directory's web.config apply when the section is asked
// for by path, outside a request?
//
// Samples/WebFormsSample/Secure/web.config denies anonymous users. The application root's inherited
// configuration allows everyone. So the two must give different answers for the two paths, and this
// pins down which path FORMS the lookup actually honours - the thing that was previously recorded as
// a known unknown.
//

using System.Configuration;
using System.Linq;
using System.Web.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace WebFormsPort.ConfigurationTests
{
	[Collection (ConfigurationCollection.Name)]
	public class DirectoryScopedConfigurationTests
	{
		readonly ConfigurationFixture fixture;
		readonly ITestOutputHelper output;

		public DirectoryScopedConfigurationTests (ConfigurationFixture fixture, ITestOutputHelper output)
		{
			this.fixture = fixture;
			this.output = output;
		}

		static bool DeniesAnonymous (AuthorizationSection section)
		{
			return section != null && section.Rules.Cast<AuthorizationRule> ()
				.Any (rule => rule.Action == AuthorizationRuleAction.Deny && rule.Users.Contains ("?"));
		}

		static AuthorizationSection Authorization (string path)
		{
			return (AuthorizationSection) WebConfigurationManager.GetSection ("system.web/authorization", path);
		}

		[Theory]
		[InlineData ("/Secure")]
		[InlineData ("/Secure/")]
		[InlineData ("~/Secure")]
		public void Sub_directory_web_config_applies_when_the_path_names_a_directory (string path)
		{
			// Secure/web.config's <deny users="?"/> has to be visible for the directory itself.
			Assert.True (DeniesAnonymous (Authorization (path)),
				     "expected the Secure/ directory's deny rule for path " + path);
		}

		[Fact]
		public void Application_root_still_allows_everyone ()
		{
			// The sub-directory's denial must not leak upwards, or the whole site would be locked.
			Assert.False (DeniesAnonymous (Authorization ("/")),
				      "the Secure/ deny rule leaked to the application root");
		}

		[Fact]
		public void Path_naming_a_file_resolves_to_its_directory ()
		{
			// This is the form UrlAuthorizationModule effectively asks for during a request, and the
			// one whose behaviour was previously unexplained.
			output.WriteLine ("deny for /Secure/Secret.aspx: " +
					  DeniesAnonymous (Authorization ("/Secure/Secret.aspx")));

			Assert.True (DeniesAnonymous (Authorization ("/Secure/Secret.aspx")),
				     "a path naming a file did not pick up its own directory's web.config");
		}
	}
}
