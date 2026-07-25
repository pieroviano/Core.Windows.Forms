//
// Diagnostic for the ported configuration system.
//
// Initialises the runtime against an application directory and reads the sections that must work
// before anything can be served, printing the FULL exception chain. The configuration system wraps
// failures in "An unexpected error occurred in 'Configuration::ctor'", which hides the real cause,
// so unwrapping it here is the difference between a five-minute fix and an afternoon.
//
// Usage: dotnet run --project tools/verify-config -- <app-physical-path>
//

using System;
using System.Configuration;
using System.IO;
using System.Web.Configuration;
using System.Web.Hosting.Kestrel;

internal static class Program
{
	static int Main (string [] args)
	{
		string appPath = args.Length > 0
			? Path.GetFullPath (args [0])
			: Path.GetFullPath (Path.Combine (AppContext.BaseDirectory, "..", "..", "..", "..", "..",
							  "samples", "WebFormsSample"));

		Console.WriteLine ("app path : " + appPath);

		try {
			WebFormsRuntimeHost.Initialize (appPath, "/", "verify-config");
			Console.WriteLine ("initialise: OK");
		} catch (Exception e) {
			Console.WriteLine ("initialise: FAILED");
			Dump (e, 1);
			return 1;
		}

		Console.WriteLine ("machine.config : " + WebFormsRuntimeHost.MachineConfigPath);
		Console.WriteLine ("root web.config: " + WebFormsRuntimeHost.RootWebConfigPath);
		Console.WriteLine ("app web.config : " + Path.Combine (appPath, "web.config"));

		// The sections the request pipeline cannot start without. httpHandlers is the one that maps
		// *.aspx to PageHandlerFactory; httpModules is instantiated wholesale at application start.
		string [] sections = {
			"system.web/httpRuntime",
			"system.web/compilation",
			"system.web/httpHandlers",
			"system.web/httpModules",
			"system.web/pages",
			"system.web/authentication",
			"system.web/authorization",
			"system.web/sessionState",
			"system.web/machineKey",
			"system.web/globalization",
			"system.web/customErrors",
			"system.web/trace",
			"system.web/webControls",
			"system.web/clientTarget",
			"system.web/siteMap",
			"system.web/anonymousIdentification",
			"system.web/profile",
			"system.web/roleManager",
			"system.web/membership",
			"system.web/caching/outputCache",
		};

		int failures = 0;
		foreach (string name in sections) {
			try {
				object section = WebConfigurationManager.GetWebApplicationSection (name);
				Console.WriteLine ("  {0,-42} {1}", name,
						   section == null ? "<null>" : section.GetType ().Name);
			} catch (Exception e) {
				failures++;
				Console.WriteLine ("  {0,-42} FAILED", name);
				Dump (e, 2);
			}
		}

		try {
			var appSettings = System.Configuration.ConfigurationManager.AppSettings;
			Console.WriteLine ("ConfigurationManager.AppSettings routed through System.Web: {0} key(s)",
					   appSettings == null ? -1 : appSettings.Count);
		} catch (Exception e) {
			failures++;
			Console.WriteLine ("ConfigurationManager.AppSettings: FAILED");
			Dump (e, 1);
		}

		Console.WriteLine (failures == 0
			? "\nall sections resolved"
			: String.Format ("\n{0} section(s) failed", failures));
		return failures == 0 ? 0 : 1;
	}

	// The configuration system nests the interesting exception two or three levels down.
	static void Dump (Exception e, int indent)
	{
		string pad = new string (' ', indent * 4);
		int depth = 0;
		while (e != null) {
			Console.WriteLine ("{0}[{1}] {2}: {3}", pad, depth, e.GetType ().FullName, e.Message);
			var cee = e as ConfigurationErrorsException;
			if (cee != null && !String.IsNullOrEmpty (cee.Filename))
				Console.WriteLine ("{0}    at {1} line {2}", pad, cee.Filename, cee.Line);
			if (e.StackTrace != null) {
				foreach (string line in e.StackTrace.Split ('\n')) {
					string t = line.TrimEnd ();
					if (t.Length == 0)
						continue;
					Console.WriteLine ("{0}    {1}", pad, t.Trim ());
				}
			}
			e = e.InnerException;
			depth++;
		}
	}
}
