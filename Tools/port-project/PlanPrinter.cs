//
// Printing and executing a plan.
//
// Both consume the same ConversionPlan. PlanPrinter is what dry-run shows; PlanExecutor is what
// --apply does. Neither decides anything.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PortProject
{
	static class PlanPrinter
	{
		public static void Print (ConversionPlan plan, bool applied, TextWriter output)
		{
			output.WriteLine ();
			output.WriteLine (applied ? "Converted" : "Would convert");
			output.WriteLine ("  project     : " + plan.ProjectPath);
			output.WriteLine ("  application : " + plan.ApplicationDirectory);
			output.WriteLine ();

			if (plan.Actions.Count > 0) {
				output.WriteLine (applied ? "Changes made" : "Changes that would be made");
				foreach (FileAction action in plan.Actions) {
					string name = Path.GetFileName (action.Path);
					switch (action.Kind) {
					case ActionKind.Rename:
						output.WriteLine ("  rename  " + name + " -> " +
								  Path.GetFileName (action.Destination));
						break;
					case ActionKind.Write:
						output.WriteLine ("  write   " + name);
						break;
					case ActionKind.Skip:
						output.WriteLine ("  skip    " + name);
						break;
					}

					if (!String.IsNullOrEmpty (action.Reason))
						output.WriteLine ("            " + action.Reason);
				}
				output.WriteLine ();
			}

			PrintFindings (plan, Severity.Info, "What was decided, and why", output);

			// NeedsAttention last, so it is the thing left on screen. These are the whole reason a
			// best-effort conversion is safe to run: they are what the tool could not decide for you.
			PrintFindings (plan, Severity.Blocker, "BLOCKERS - this application cannot be ported as-is", output);
			PrintFindings (plan, Severity.NeedsAttention, "NEEDS ATTENTION - converted, but check these", output);

			output.WriteLine (Summary (plan, applied));
		}

		static void PrintFindings (ConversionPlan plan, Severity severity, string heading, TextWriter output)
		{
			var findings = plan.Findings.Where (f => f.Severity == severity).ToArray ();
			if (findings.Length == 0)
				return;

			output.WriteLine (heading);
			foreach (Finding finding in findings) {
				output.Write ("  - " + finding.Title);
				if (!String.IsNullOrEmpty (finding.Evidence))
					output.Write ("  [" + finding.Evidence + "]");
				output.WriteLine ();

				if (!String.IsNullOrEmpty (finding.Detail))
					foreach (string line in Wrap (finding.Detail, 92))
						output.WriteLine ("      " + line);

				if (!String.IsNullOrEmpty (finding.GuideSection))
					output.WriteLine ("      see " + finding.GuideSection);
			}

			output.WriteLine ();
		}

		public static string Summary (ConversionPlan plan, bool applied)
		{
			if (plan.HasBlockers)
				return "BLOCKED: " + plan.Findings.Count (f => f.Severity == Severity.Blocker) +
				       " blocker(s). Nothing was changed.";

			int attention = plan.NeedsAttentionCount;
			string verb = applied ? "Converted" : "Dry run";
			string tail = attention == 0
				? "nothing needs manual attention."
				: attention + " item(s) need manual attention - see above.";

			return verb + ": " + plan.Actions.Count (a => a.Kind != ActionKind.Skip) + " file change(s), " + tail +
			       (applied ? "" : "  Re-run with --apply to perform it.");
		}

		public static string ToJson (ConversionPlan plan, bool applied)
		{
			var payload = new {
				project = plan.ProjectPath,
				applicationDirectory = plan.ApplicationDirectory,
				applied,
				blocked = plan.HasBlockers,
				actions = plan.Actions.Select (a => new {
					kind = a.Kind.ToString (),
					path = a.Path,
					destination = a.Destination,
					reason = a.Reason,
				}),
				findings = plan.Findings.Select (f => new {
					severity = f.Severity.ToString (),
					title = f.Title,
					detail = f.Detail,
					evidence = f.Evidence,
					guide = f.GuideSection,
				}),
			};

			return JsonSerializer.Serialize (payload, new JsonSerializerOptions { WriteIndented = true });
		}

		static IEnumerable<string> Wrap (string text, int width)
		{
			var line = new StringBuilder ();

			foreach (string word in text.Split (' ')) {
				if (line.Length > 0 && line.Length + 1 + word.Length > width) {
					yield return line.ToString ();
					line.Clear ();
				}

				if (line.Length > 0)
					line.Append (' ');

				line.Append (word);
			}

			if (line.Length > 0)
				yield return line.ToString ();
		}
	}

	static class PlanExecutor
	{
		/// <summary>
		/// Performs the plan. Renames happen before writes, which is what lets a Write target the same
		/// path its Rename just vacated - the ordering the planner emits them in is the ordering here.
		/// </summary>
		public static void Execute (ConversionPlan plan)
		{
			if (plan.HasBlockers)
				throw new InvalidOperationException (
					"Refusing to execute a plan with blockers. This is a bug in the caller: " +
					"Program checks for blockers before getting here.");

			foreach (FileAction action in plan.Actions) {
				switch (action.Kind) {
				case ActionKind.Rename:
					if (File.Exists (action.Destination))
						File.Delete (action.Destination);   // --force was required to get here

					File.Move (action.Path, action.Destination);
					break;

				case ActionKind.Write:
					string directory = Path.GetDirectoryName (action.Path);
					if (!String.IsNullOrEmpty (directory))
						Directory.CreateDirectory (directory);

					// UTF-8 without BOM: an MSBuild project file with a BOM is legal but noisy in
					// diffs, and Program.cs never needs one.
					File.WriteAllText (action.Path, action.Content, new UTF8Encoding (false));
					break;

				case ActionKind.Skip:
					break;
				}
			}
		}
	}
}
