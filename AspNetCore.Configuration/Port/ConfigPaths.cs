//
// Replacement for the two path APIs Mono's System.Configuration reads that do not work on .NET Core.
//
//   RuntimeEnvironment.SystemConfigurationFile  - throws PlatformNotSupportedException.
//   AppDomainSetup.ConfigurationFile            - does not exist.
//
// Both are host policy, not configuration logic, so they are hoisted here and
// tools/port-patches.txt redirects the upstream reads. Core.Web assigns MachineConfigPath during
// initialisation (it owns the embedded machine.config and extracts it); the defaults below keep this
// assembly usable on its own, which matters because it must not depend on Core.Web - the reference
// runs the other way.
//

using System;
using System.IO;
using System.Reflection;

namespace System.Configuration.Internal
{
	static class PortConfigPaths
	{
		static string machine_config_path;
		static string exe_configuration_file;

		/// <summary>
		/// Machine-level configuration file. Core.Web sets this to the machine.config it extracts
		/// from its embedded resources; standalone use falls back to a file beside the application.
		/// </summary>
		public static string MachineConfigPath {
			get {
				if (machine_config_path == null)
					machine_config_path = Path.Combine (AppContext.BaseDirectory, "machine.config");
				return machine_config_path;
			}
			set { machine_config_path = value; }
		}

		/// <summary>
		/// The .exe.config an ExeConfigurationHost would read. Meaningless for a web application -
		/// web.config is the configuration source - but OpenExeConfiguration and the legacy
		/// ConfigurationSettings path still ask for it.
		/// </summary>
		public static string ExeConfigurationFile {
			get {
				if (exe_configuration_file == null) {
					Assembly entry = Assembly.GetEntryAssembly ();
					string name = entry != null ? entry.GetName ().Name : "application";
					exe_configuration_file = Path.Combine (AppContext.BaseDirectory, name + ".dll.config");
				}
				return exe_configuration_file;
			}
			set { exe_configuration_file = value; }
		}
	}
}
