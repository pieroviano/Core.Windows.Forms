//
// port-project - automates PORTING-GUIDE.md.
//
// Takes an existing ASP.NET .csproj or .vbproj, renames it to *.old, and writes an SDK-style project,
// a Kestrel host and the mechanical web.config edits in its place - reporting everything it could not
// decide on its own.
//
// Dry-run by default. A tool that rewrites project files should not do so because of a typo in a path,
// and the preview doubles as the report you have to read anyway.
//
// Usage: dotnet run --project Tools/port-project -- <path> [--apply]
//

using System;
using System.IO;
using System.Linq;

namespace PortProject
{
	static class Program
	{
		const int ExitSuccess = 0;
		const int ExitUsage = 1;
		const int ExitBlocked = 2;

		static int Main (string [] args)
		{
			if (args.Length == 0 || args.Any (a => a == "-h" || a == "--help")) {
				PrintUsage ();
				return args.Length == 0 ? ExitUsage : ExitSuccess;
			}

			string target = null;
			var options = new PlannerOptions ();
			bool apply = false;
			bool json = false;

			for (int i = 0; i < args.Length; i++) {
				string arg = args [i];

				switch (arg) {
				case "--apply":
					apply = true;
					break;
				case "--force":
					options.Force = true;
					break;
				case "--no-program":
					options.GenerateProgram = false;
					break;
				case "--no-web-config":
					options.RewriteWebConfig = false;
					break;
				case "--json":
					json = true;
					break;
				case "--package-version":
					if (i + 1 >= args.Length) {
						Console.Error.WriteLine ("--package-version needs a value.");
						return ExitUsage;
					}

					options.PackageVersion = args [++i];
					break;
				default:
					if (arg.StartsWith ("-", StringComparison.Ordinal)) {
						Console.Error.WriteLine ("Unknown option: " + arg);
						PrintUsage ();
						return ExitUsage;
					}

					if (target != null) {
						Console.Error.WriteLine ("More than one path given: " + target + " and " + arg);
						return ExitUsage;
					}

					target = arg;
					break;
				}
			}

			if (target == null) {
				Console.Error.WriteLine ("No project or directory given.");
				PrintUsage ();
				return ExitUsage;
			}

			string projectPath;
			try {
				projectPath = ResolveProject (target);
			} catch (Exception e) {
				Console.Error.WriteLine (e.Message);
				return ExitUsage;
			}

			ConversionPlan plan = Planner.Plan (projectPath, options);

			// Blockers stop the run before anything is written, in BOTH modes. --apply is not an
			// override: an application with a remoting endpoint does not become portable because
			// someone passed a flag.
			bool executed = false;
			if (apply && !plan.HasBlockers) {
				try {
					PlanExecutor.Execute (plan);
					executed = true;
				} catch (Exception e) {
					Console.Error.WriteLine ("Failed while applying: " + e.Message);
					Console.Error.WriteLine ("Some changes may have been made. The originals are the *.old files.");
					return ExitUsage;
				}
			}

			if (json)
				Console.WriteLine (PlanPrinter.ToJson (plan, executed));
			else
				PlanPrinter.Print (plan, executed, Console.Out);

			return plan.HasBlockers ? ExitBlocked : ExitSuccess;
		}

		/// <summary>
		/// Accepts a project file or the directory holding one. A directory with two project files is an
		/// error rather than a guess - converting the wrong one is worse than asking.
		/// </summary>
		static string ResolveProject (string target)
		{
			string full = Path.GetFullPath (target);

			if (File.Exists (full))
				return full;

			if (!Directory.Exists (full))
				throw new FileNotFoundException ("No such file or directory: " + full);

			string [] projects = Directory.GetFiles (full, "*.csproj")
				.Concat (Directory.GetFiles (full, "*.vbproj"))
				.OrderBy (p => p, StringComparer.OrdinalIgnoreCase)
				.ToArray ();

			if (projects.Length == 0)
				throw new FileNotFoundException ("No .csproj or .vbproj in " + full);

			if (projects.Length > 1)
				throw new InvalidOperationException (
					"More than one project in " + full + ":" + Environment.NewLine + "  " +
					String.Join (Environment.NewLine + "  ", projects.Select (Path.GetFileName)) +
					Environment.NewLine + "Name the one you mean.");

			return projects [0];
		}

		static void PrintUsage ()
		{
			Console.WriteLine (@"
port-project - convert an ASP.NET project to run on this port, per PORTING-GUIDE.md

  port-project <path-to-.csproj|.vbproj|directory> [options]

The original project is renamed to <name>.csproj.old and a new SDK-style project is written in
its place, together with a Kestrel host and the mechanical web.config assembly renames.

Options
  --apply                 perform the conversion (WITHOUT THIS IT ONLY PREVIEWS)
  --force                 overwrite an existing .old backup
  --no-program            do not generate Program.cs
  --no-web-config         do not rewrite web.config
  --package-version <v>   port package version to reference (default 1.0.0)
  --json                  emit the plan and findings as JSON
  -h, --help              this text

Exit codes
  0  converted, or previewed with no blockers
  1  usage or I/O error
  2  blockers found - nothing was changed
".TrimStart ());
		}
	}
}
