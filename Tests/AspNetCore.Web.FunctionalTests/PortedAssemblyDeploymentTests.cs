//
// Regression tests for the port's DEPLOYMENT contract.
//
// Written after a real bug: Core.Web.Extensions had no ProjectReference from
// AspNetCore.Web.Hosting.Kestrel, so it never reached any application's output directory. The
// explanatory comment for the reference was still in the csproj; only the reference itself was gone.
//
// The consequence was out of all proportion to the cause. gen-config.ps1 retargets root-web.config's
// ScriptModule-4.0 registration at Core.Web.Extensions, and <httpModules> entries are instantiated at
// APPLICATION START - so the missing assembly produced FileNotFoundException on every request, for
// pages that had nothing to do with AJAX. The whole sample application was dead.
//
// A functional test would have caught it, but as fifty simultaneous failures with a confusing cause.
// These assert the contract directly, so the next occurrence names itself.
//

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class PortedAssemblyDeploymentTests
	{
		readonly SampleAppFixture fixture;

		public PortedAssemblyDeploymentTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Theory]
		[InlineData ("Core.Web")]
		[InlineData ("Core.Configuration")]
		[InlineData ("Core.Web.Services")]
		[InlineData ("Core.Web.Extensions")]
		[InlineData ("Core.Web.Hosting.Kestrel")]
		[InlineData ("Core.Web.ConfigBridge")]
		public void Ported_assembly_reaches_the_output_directory (string name)
		{
			// Several of these are never compiled against - they are resolved BY NAME out of the
			// generated configuration - so nothing but this would notice their absence at build time.
			string path = Path.Combine (AppContext.BaseDirectory, name + ".dll");

			Assert.True (File.Exists (path),
				     name + ".dll is missing from " + AppContext.BaseDirectory +
				     ". It is resolved by name from the generated root-web.config, so its " +
				     "ProjectReference cannot be dropped even though no code references it.");
		}

		[Fact]
		public async Task Extensions_assembly_is_loaded_once_the_application_has_started ()
		{
			await WarmUpAsync ();

			// root-web.config registers ScriptModule-4.0 from Core.Web.Extensions in <httpModules>,
			// and modules are instantiated when the HttpApplication is built. So once a single
			// request has been served the assembly must be loaded - if it is not, either the
			// registration was lost from the generated configuration or the module never ran.
			Assert.Contains (AppDomain.CurrentDomain.GetAssemblies (),
					 a => a.GetName ().Name == "Core.Web.Extensions");
		}

		[Fact]
		public async Task ScriptModule_type_resolves_from_the_ported_assembly ()
		{
			await WarmUpAsync ();

			Assembly extensions = AppDomain.CurrentDomain.GetAssemblies ()
				.FirstOrDefault (a => a.GetName ().Name == "Core.Web.Extensions");

			Assert.NotNull (extensions);

			// The exact type root-web.config names. Its namespace is unchanged by the port; only the
			// assembly was renamed.
			Assert.NotNull (extensions.GetType ("System.Web.Handlers.ScriptModule", throwOnError: false));
		}

		/// <summary>
		/// Serves one request, so that the assertions above do not depend on some other test class
		/// having run first.
		/// </summary>
		/// <remarks>
		/// WebFormsRuntimeHost.Initialize does NOT build the HttpApplication - HttpApplicationFactory
		/// does that on the first request, and that is when &lt;httpModules&gt; are instantiated and
		/// their assemblies loaded. Asserting on loaded assemblies straight after the fixture starts
		/// passes only when an earlier test happened to make a request first.
		/// </remarks>
		async Task WarmUpAsync ()
		{
			using System.Net.Http.HttpClient client = fixture.CreateClient ();
			await client.GetAsync ("/hello.hello");
		}

		[Fact]
		public void Ported_System_Web_is_the_port_and_not_the_empty_framework_facade ()
		{
			// .NET ships an EMPTY System.Web.dll facade with zero exported types. If Build/
			// WebFormsPort.targets were not imported, HttpContext would bind to that instead and the
			// failure would be a TypeLoadException on the first request.
			Assembly systemWeb = typeof (System.Web.HttpContext).Assembly;

			Assert.Equal ("Core.Web", systemWeb.GetName ().Name);
			Assert.NotEmpty (systemWeb.GetExportedTypes ());
		}

		[Fact]
		public void Ported_System_Configuration_is_the_port_and_not_the_empty_framework_facade ()
		{
			Assembly configuration = typeof (System.Configuration.ConfigurationSection).Assembly;

			Assert.Equal ("Core.Configuration", configuration.GetName ().Name);
		}
	}
}
