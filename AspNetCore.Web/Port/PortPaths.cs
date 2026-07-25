//
// Replacement for the AppDomainSetup properties the upstream tree reads.
//
// On .NET Framework each ASP.NET application got its own AppDomain whose AppDomainSetup carried
// DynamicBase (the "Temporary ASP.NET Files" directory), PrivateBinPath (bin) and ApplicationName.
// AppDomain.CurrentDomain.SetupInformation still exists on .NET Core but those three properties
// are meaningless there, so tools/port-patches.txt rewrites every read of them to the members
// below. WebFormsRuntimeHost assigns them during initialisation.
//
// ApplicationBase is *not* redirected - it works correctly on .NET Core and upstream reads it
// directly.
//

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace System.Web.Util
{
	static class PortPaths
	{
		static string dynamic_base;
		static string private_bin_path;
		static string application_name;

		// "Temporary ASP.NET Files" root: where generated .cs, compiled .dll and the batch-
		// compilation bookkeeping files live. Defaults to a per-application-base directory under
		// the user's temp so that compilation works even if the host forgot to initialise us.
		internal static string DynamicBase {
			get {
				if (dynamic_base == null)
					dynamic_base = DefaultDynamicBase ();
				return dynamic_base;
			}
			set { dynamic_base = value; }
		}

		internal static string PrivateBinPath {
			get {
				if (private_bin_path == null)
					private_bin_path = Path.Combine (AppContext.BaseDirectory, "bin");
				return private_bin_path;
			}
			set { private_bin_path = value; }
		}

		// Machine-level configuration file. Upstream reached this through
		// RuntimeEnvironment.SystemConfigurationFile, which throws on .NET Core; MachineConfig
		// extracts an embedded copy instead. Assignable so a host can substitute its own.
		internal static string MachineConfigPath {
			get { return MachineConfig.Path; }
			set { MachineConfig.Path = value; }
		}

		internal static string ApplicationName {
			get {
				if (application_name == null)
					application_name = "ROOT";
				return application_name;
			}
			set { application_name = value; }
		}

		static string DefaultDynamicBase ()
		{
			string appBase = HttpRuntime.AppDomainAppPath;
			if (String.IsNullOrEmpty (appBase))
				appBase = AppContext.BaseDirectory;

			return Path.Combine (
				Path.GetTempPath (),
				"aspnet-webforms",
				ShortHash (appBase),
				"assembly");
		}

		// Stable, filesystem-safe discriminator for an application's physical path, so two apps
		// hosted from the same machine do not share a codegen directory.
		static string ShortHash (string value)
		{
			using (SHA256 sha = SHA256.Create ()) {
				byte [] hash = sha.ComputeHash (Encoding.UTF8.GetBytes (value.ToLowerInvariant ()));
				StringBuilder sb = new StringBuilder (16);
				for (int i = 0; i < 8; i++)
					sb.Append (hash [i].ToString ("x2"));
				return sb.ToString ();
			}
		}
	}
}
