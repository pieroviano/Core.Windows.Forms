//
// Core.Web.ConfigBridge: pointing the SHIPPING System.Configuration.ConfigurationManager at the
// ported configuration system.
//
// Install is once-per-process, so the ordering here matters and the tests are deliberately in one
// non-parallel collection: Installs_once runs the install, everything else observes the result.
// xunit orders tests within a class unpredictably, so each test that needs the bridge installs it
// first - Install is idempotent and returns true when it is already in place.
//

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Web.Configuration.Bridge;
using Xunit;

namespace WebFormsPort.ConfigBridgeTests
{
	[Collection (BridgeCollection.Name)]
	public class PackagedConfigurationBridgeTests
	{
		const string SettingKey = "bridge.setting";
		const string SettingValue = "routed-through-the-bridge";
		const string ConnectionName = "BridgeDb";

		static NameValueCollection AppSettings ()
		{
			return new NameValueCollection { { SettingKey, SettingValue } };
		}

		static IEnumerable<BridgedConnectionString> ConnectionStrings ()
		{
			yield return new BridgedConnectionString (ConnectionName, "Server=.;Database=bridge",
								 "System.Data.SqlClient");
		}

		static bool Install ()
		{
			return PackagedConfigurationBridge.Install (AppSettings, ConnectionStrings);
		}

		[Fact]
		public void The_ConfigurationManager_under_test_is_the_packaged_one ()
		{
			// If this ever says Core.Configuration the rest of the file is testing the wrong
			// assembly, and the bridge's entire reason to exist has been compiled away.
			Assert.Equal ("System.Configuration.ConfigurationManager",
				      typeof (ConfigurationManager).Assembly.GetName ().Name);
		}

		[Fact]
		public void Install_succeeds_and_is_idempotent ()
		{
			Assert.True (Install (), "the runtime did not expose SetConfigurationSystem");
			// Second call must not throw or reinstall; a process hosts one configuration system.
			Assert.True (Install ());
		}

		[Fact]
		public void AppSettings_are_served_from_the_supplied_callback ()
		{
			Install ();

			// A third-party library compiled against this package reads exactly this property.
			Assert.Equal (SettingValue, ConfigurationManager.AppSettings [SettingKey]);
		}

		[Fact]
		public void ConnectionStrings_are_served_as_the_packages_own_section_type ()
		{
			Install ();

			// ConfigurationManager.ConnectionStrings casts the section to the PACKAGE's
			// ConnectionStringsSection, so the bridge has to build that exact type - returning the
			// ported one would raise InvalidCastException inside somebody else's code.
			ConnectionStringSettings settings = ConfigurationManager.ConnectionStrings [ConnectionName];

			Assert.NotNull (settings);
			Assert.Equal ("Server=.;Database=bridge", settings.ConnectionString);
			Assert.Equal ("System.Data.SqlClient", settings.ProviderName);
		}

		[Fact]
		public void GetSection_routes_appSettings_through_the_bridge ()
		{
			Install ();

			object section = ConfigurationManager.GetSection ("appSettings");

			Assert.IsAssignableFrom<NameValueCollection> (section);
			Assert.Equal (SettingValue, ((NameValueCollection) section) [SettingKey]);
		}

		[Fact]
		public void Unmapped_section_reads_as_not_configured ()
		{
			Install ();

			// Null, not an exception and not a ported section object: a section from the ported
			// implementation would be the wrong CLR type for a caller compiled against the package.
			Assert.Null (ConfigurationManager.GetSection ("system.web/httpRuntime"));
		}

		[Fact]
		public void Install_rejects_missing_callbacks ()
		{
			Assert.Throws<System.ArgumentNullException> (
				() => PackagedConfigurationBridge.Install (null, ConnectionStrings));
			Assert.Throws<System.ArgumentNullException> (
				() => PackagedConfigurationBridge.Install (AppSettings, null));
		}
	}

	/// <summary>
	/// One collection, so these never run in parallel with each other: they share the process-wide
	/// configuration system that Install replaces.
	/// </summary>
	[CollectionDefinition (Name, DisableParallelization = true)]
	public sealed class BridgeCollection
	{
		public const string Name = "packaged-configuration-bridge";
	}
}
