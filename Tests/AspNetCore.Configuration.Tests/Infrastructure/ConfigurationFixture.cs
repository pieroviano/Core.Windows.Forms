//
// Initialises the ported runtime against Samples/WebFormsSample without starting a server.
//
// WebConfigurationManager.Init installs HttpConfigurationSystem into System.Configuration so that
// web.config - not a nonexistent app.config - answers GetSection, and extracts the embedded
// machine.config as a side effect of the first read. All of that is process-global and one-shot,
// hence a single collection fixture for the assembly.
//

using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.ConfigurationTests
{
	public sealed class ConfigurationFixture
	{
		public ConfigurationFixture ()
		{
			// The configuration system wraps failures in "An unexpected error occurred in
			// 'Configuration::ctor'", which hides the real cause. If this throws, run
			// `dotnet run --project Tools/verify-config` - it prints the full exception chain.
			WebFormsRuntimeHost.Initialize (RepoPaths.SampleApp, "/", "ConfigurationTests");
		}

		public string AppPhysicalPath {
			get { return RepoPaths.SampleApp; }
		}

		public string MachineConfigPath {
			get { return WebFormsRuntimeHost.MachineConfigPath; }
		}

		public string RootWebConfigPath {
			get { return WebFormsRuntimeHost.RootWebConfigPath; }
		}
	}

	[CollectionDefinition (Name)]
	public sealed class ConfigurationCollection : ICollectionFixture<ConfigurationFixture>
	{
		public const string Name = "ported-configuration";
	}
}
