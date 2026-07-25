//
// The sections the request pipeline cannot start without, read through the ported configuration
// system exactly as HttpApplication reads them.
//

using System.Configuration;
using System.Linq;
using System.Web.Configuration;
using Xunit;

namespace WebFormsPort.ConfigurationTests
{
	[Collection (ConfigurationCollection.Name)]
	public class ConfigurationSectionTests
	{
		readonly ConfigurationFixture fixture;

		public ConfigurationSectionTests (ConfigurationFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public void AppSettings_comes_from_web_config_not_app_config ()
		{
			// The port has no app.config. If HttpConfigurationSystem were not installed this would be
			// empty rather than wrong, which is why it is asserted by value.
			Assert.Equal ("seen-by-third-party-library",
				      WebConfigurationManager.AppSettings ["probe.setting"]);
		}

		[Fact]
		public void ConnectionStrings_come_from_web_config ()
		{
			ConnectionStringSettings probe = WebConfigurationManager.ConnectionStrings ["ProbeDb"];

			Assert.NotNull (probe);
			Assert.Equal ("Server=.;Database=probe", probe.ConnectionString);
			Assert.Equal ("System.Data.SqlClient", probe.ProviderName);
		}

		[Fact]
		public void AppSettings_returns_a_NameValueCollection_not_a_DefaultSection ()
		{
			// Regression guard for the gen-config.ps1 retargeting: when a type= reference binds to the
			// empty framework System.Configuration facade instead of Core.Configuration, the section
			// silently degrades to DefaultSection and appSettings comes back as the wrong type.
			object section = WebConfigurationManager.GetSection ("appSettings");

			Assert.IsAssignableFrom<System.Collections.Specialized.NameValueCollection> (section);
		}

		[Theory]
		// httpHandlers is what maps *.aspx to PageHandlerFactory; httpModules is instantiated
		// wholesale at application start, so a bad entry there takes the whole application down.
		[InlineData ("system.web/httpRuntime")]
		[InlineData ("system.web/httpHandlers")]
		[InlineData ("system.web/httpModules")]
		[InlineData ("system.web/compilation")]
		[InlineData ("system.web/authentication")]
		[InlineData ("system.web/pages")]
		[InlineData ("system.web/sessionState")]
		[InlineData ("system.web/globalization")]
		public void Section_materialises (string name)
		{
			Assert.NotNull (WebConfigurationManager.GetSection (name));
		}

		[Fact]
		public void Application_web_config_overrides_the_root_web_config ()
		{
			var compilation = (CompilationSection) WebConfigurationManager.GetSection ("system.web/compilation");

			// The sample sets debug="true" defaultLanguage="c#"; the inherited root config does not.
			Assert.NotNull (compilation);
			Assert.True (compilation.Debug);
		}

		[Fact]
		public void Authentication_section_reflects_the_forms_configuration ()
		{
			var authentication = (AuthenticationSection) WebConfigurationManager.GetSection ("system.web/authentication");

			Assert.Equal (AuthenticationMode.Forms, authentication.Mode);
			Assert.Equal (".WEBFORMSAUTH", authentication.Forms.Name);
			Assert.Equal ("Login.aspx", authentication.Forms.LoginUrl);
		}

		[Fact]
		public void Custom_module_registration_from_the_application_config_is_visible ()
		{
			var modules = (HttpModulesSection) WebConfigurationManager.GetSection ("system.web/httpModules");

			// The application's own <httpModules> entry, merged on top of the inherited root config.
			Assert.Contains (modules.Modules.Cast<HttpModuleAction> (),
					 module => module.Name == "TraceModule" &&
						   module.Type.StartsWith ("WebFormsSample.TraceModule"));
		}

		[Fact]
		public void Custom_handler_registration_from_the_application_config_is_visible ()
		{
			var handlers = (HttpHandlersSection) WebConfigurationManager.GetSection ("system.web/httpHandlers");

			Assert.Contains (handlers.Handlers.Cast<HttpHandlerAction> (),
					 handler => handler.Path == "hello.hello" &&
						    handler.Type.StartsWith ("WebFormsSample.HelloHandler"));
		}

		[Fact]
		public void Inherited_root_config_still_maps_aspx_to_the_page_handler_factory ()
		{
			var handlers = (HttpHandlersSection) WebConfigurationManager.GetSection ("system.web/httpHandlers");

			// Comes from the generated root web.config, not the application's - so this is the
			// inheritance chain working, and it is what makes any page servable at all.
			Assert.Contains (handlers.Handlers.Cast<HttpHandlerAction> (),
					 handler => handler.Path == "*.aspx" && handler.Type.Contains ("PageHandlerFactory"));
		}

		[Fact]
		public void Application_root_authorization_allows_everyone ()
		{
			var authorization = (AuthorizationSection) WebConfigurationManager.GetSection ("system.web/authorization");

			// From the inherited root web.config. The sub-directory denial in Secure/web.config must
			// not leak upwards, or the whole site would be locked.
			Assert.NotNull (authorization);
			Assert.DoesNotContain (authorization.Rules.Cast<AuthorizationRule> (),
					       rule => rule.Action == AuthorizationRuleAction.Deny && rule.Users.Contains ("?"));
		}

		// NOTE: directory-scoped lookup - WebConfigurationManager.GetSection (name, "/Secure/Secret.aspx")
		// - returned the application-root section rather than Secure/web.config's when called outside a
		// request. That path is exercised for real by the functional suite instead, where
		// UrlAuthorizationModule resolves configuration against the live request and correctly bounces
		// an anonymous caller. Deliberately not asserted here: the behaviour is not understood well
		// enough to pin down, and a test asserting the observed result would freeze a possible bug.
		// See LIMITATIONS.md.
	}
}
