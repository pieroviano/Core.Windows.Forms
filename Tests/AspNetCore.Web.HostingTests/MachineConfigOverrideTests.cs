//
// WebFormsOptions.MachineConfigPath - machine-level configuration supplied by the host.
//

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Configuration;
using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.HostingTests
{
	[Collection (HostingCollection.Name)]
	public class MachineConfigOverrideTests
	{
		readonly AuthenticatedHostFixture fixture;

		public MachineConfigOverrideTests (AuthenticatedHostFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public void Runtime_uses_the_host_supplied_file ()
		{
			Assert.Equal (Path.GetFullPath (fixture.MachineConfigPath),
				      Path.GetFullPath (WebFormsRuntimeHost.MachineConfigPath));
		}

		[Fact]
		public void Host_supplied_file_is_not_overwritten_by_the_embedded_copy ()
		{
			string text = File.ReadAllText (WebFormsRuntimeHost.MachineConfigPath);

			Assert.Contains (AuthenticatedHostFixture.MachineSettingKey, text);
		}

		[Fact]
		public async Task Settings_from_the_host_supplied_machine_config_are_visible_to_the_application ()
		{
			// Warm the application: configuration is read on the first request.
			using HttpClient client = fixture.CreateClient ();
			await client.GetAsync ("/Simple.aspx");

			// Inherited from machine.config, not declared in the application's own web.config.
			Assert.Equal (AuthenticatedHostFixture.MachineSettingValue,
				      WebConfigurationManager.AppSettings [AuthenticatedHostFixture.MachineSettingKey]);
		}

		[Fact]
		public async Task Application_web_config_still_wins_over_machine_config ()
		{
			using HttpClient client = fixture.CreateClient ();
			await client.GetAsync ("/Simple.aspx");

			// The sample's own web.config setting must survive the substitution - a replacement
			// machine.config changes what is INHERITED, not what the application declares.
			Assert.Equal ("seen-by-third-party-library",
				      WebConfigurationManager.AppSettings ["probe.setting"]);
		}

		[Fact]
		public async Task Pages_still_serve_with_a_substituted_machine_config ()
		{
			using HttpClient client = fixture.CreateClient ();

			// The root web.config next to machine.config is what maps *.aspx to PageHandlerFactory.
			// If the substitution broke that association nothing would be served at all.
			string body = await client.GetStringAsync ("/Simple.aspx");

			Assert.Contains ("<h1>Simple page</h1>", body);
		}

		// NOTE: the startup validation of MachineConfigPath - missing file, or one that does not parse
		// as XML - is deliberately not asserted here. Initialize runs once per process and has
		// already run by the time any test executes, so exercising the rejection path would need a
		// whole additional test project whose only job is to fail to start. The validation itself is
		// in WebFormsRuntimeHost.Initialize and is worth having: a malformed machine.config disables
		// every configuration section at once, and the resulting 500 names nothing.
	}
}
