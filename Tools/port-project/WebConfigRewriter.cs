//
// web.config: the four mechanical rewrites, and everything else reported rather than edited.
//
// This is the highest-risk file in the tool, because a wrong web.config does not fail the build - it
// fails at APPLICATION START, after everything looked fine. So the rule it follows is narrow on
// purpose: only the assembly-name substitutions from PORTING-GUIDE.md step 4, which are pure text and
// have exactly one right answer, are applied.
//
// <system.webServer> is READ AND REPORTED, NEVER EDITED. An IIS <rewrite> ruleset has no mechanical
// translation into ASP.NET Core middleware; a half-translated one is worse than an untouched one,
// because it looks done. The tool prints each child element beside the row of the guide's table that
// tells you what it becomes, and leaves the XML alone.
//
// The rewrite is done on TEXT, not on an XDocument. Round-tripping through XDocument reformats
// attribute quoting, self-closing tags and whitespace across the whole file, which turns a four-line
// semantic change into an unreviewable diff. Regex over the raw text keeps the diff to the lines that
// actually changed.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PortProject
{
	/// <summary>What the planner needs to know about an application's web.config.</summary>
	sealed class WebConfigFacts
	{
		public static readonly WebConfigFacts None = new WebConfigFacts ();

		public bool Exists { get; private set; }

		public string Path { get; private set; }

		public string Evidence { get; private set; }

		/// <summary>"StateServer" or "SQLServer" when out-of-process; null for InProc/Off/absent.</summary>
		public string OutOfProcessSessionMode { get; private set; }

		/// <summary>Child element names of &lt;system.webServer&gt;, which the tool refuses to translate.</summary>
		public IReadOnlyList<string> SystemWebServerChildren { get; private set; } = Array.Empty<string> ();

		public bool HasRemotingSection { get; private set; }

		public static WebConfigFacts Read (string path)
		{
			if (path == null || !System.IO.File.Exists (path))
				return None;

			var facts = new WebConfigFacts {
				Exists = true,
				Path = path,
				Evidence = System.IO.Path.GetFileName (path),
			};

			XDocument document;
			try {
				document = XDocument.Load (path);
			} catch (Exception) {
				// Unparseable web.config: the planner still converts the project, and reports this.
				return facts;
			}

			XElement sessionState = document.Descendants ()
				.FirstOrDefault (e => e.Name.LocalName == "sessionState");
			string mode = sessionState?.Attribute ("mode")?.Value;
			if (String.Equals (mode, "StateServer", StringComparison.OrdinalIgnoreCase) ||
			    String.Equals (mode, "SQLServer", StringComparison.OrdinalIgnoreCase))
				facts.OutOfProcessSessionMode = mode;

			XElement systemWebServer = document.Descendants ()
				.FirstOrDefault (e => e.Name.LocalName == "system.webServer");
			if (systemWebServer != null)
				facts.SystemWebServerChildren = systemWebServer.Elements ()
					.Select (e => e.Name.LocalName).Distinct ().ToArray ();

			facts.HasRemotingSection = document.Descendants ()
				.Any (e => e.Name.LocalName == "system.runtime.remoting");

			return facts;
		}
	}

	static class WebConfigRewriter
	{
		/// <summary>
		/// Assembly renames from PORTING-GUIDE.md step 4. Longest first: "System.Web.Mvc" has to be
		/// matched before "System.Web", or the shorter rule rewrites its prefix and produces
		/// "Core.Web.Mvc" from one rule and "Core.Web" + ".Mvc" from the other.
		/// </summary>
		static readonly (string Legacy, string Ported) [] AssemblyRenames = {
			("System.Web.Mvc", "Core.Web.Mvc"),
			("System.Web.Optimization", "Core.Web.Optimization"),
			("System.Web.WebPages.Razor", "Core.Web.WebPages.Razor"),
			("System.Web.WebPages.Deployment", "Core.Web.WebPages.Deployment"),
			("System.Web.WebPages", "Core.Web.WebPages"),
			("System.Web.Razor", "Core.Web.Razor"),
			("System.Web.Http.WebHost", "Core.Web.Http.WebHost"),
			("System.Web.Http", "Core.Web.Http"),
			("System.Web.Extensions", "Core.Web.Extensions"),
			("System.Web.Services", "Core.Web.Services"),
			("System.Web", "Core.Web"),
			("System.Configuration", "Core.Configuration"),
		};

		public sealed class Result
		{
			public string Content { get; set; }

			public List<string> Changes { get; } = new List<string> ();

			public bool Changed {
				get { return Changes.Count > 0; }
			}
		}

		public static Result Rewrite (string original)
		{
			var result = new Result { Content = original };

			// 1. assembly="System.Web.Mvc" -> assembly="Core.Web.Mvc"
			foreach ((string legacy, string ported) in AssemblyRenames) {
				result.Content = ReplaceCounting (
					result.Content,
					"(?<prefix>\\bassembly=\")" + Regex.Escape (legacy) + "(?<suffix>[\",])",
					"${prefix}" + ported + "${suffix}",
					count => result.Changes.Add (
						count + " x assembly=\"" + legacy + "\" -> \"" + ported + "\""));
			}

			// 2. type="X, System.Configuration" -> type="X, Core.Configuration".
			//    Type.GetType does NOT scan loaded assemblies, so leaving this one bound to the empty
			//    facade degrades the section to DefaultSection - silently.
			foreach ((string legacy, string ported) in AssemblyRenames) {
				if (legacy == "System.Web")
					continue;   // handled by rule 3, which drops the qualification entirely

				result.Content = ReplaceCounting (
					result.Content,
					"(?<prefix>,\\s*)" + Regex.Escape (legacy) + "(?<suffix>\\s*[,\"])",
					"${prefix}" + ported + "${suffix}",
					count => result.Changes.Add (
						count + " x \", " + legacy + "\" -> \", " + ported + "\""));
			}

			// 3. type="Some.Type, System.Web" -> type="Some.Type".
			//    HttpApplication.LoadType scans loaded assemblies, so the qualification is not merely
			//    wrong, it is unnecessary.
			result.Content = ReplaceCounting (
				result.Content,
				",\\s*System\\.Web\\s*(?=\")",
				"",
				count => result.Changes.Add (count + " x dropped \", System.Web\" qualification"));

			// 4. Strip Microsoft's strong name. .NET relaxes VERSION when binding but never the public
			//    key, and the port is signed with its own - so a surviving token is a hard bind failure.
			result.Content = ReplaceCounting (
				result.Content,
				",\\s*Version=[0-9.]+(,\\s*Culture=[A-Za-z-]+)?(,\\s*PublicKeyToken=[0-9a-fA-Fnull]+)?(,\\s*processorArchitecture=[A-Za-z]+)?",
				"",
				count => result.Changes.Add (count + " x stripped Version/Culture/PublicKeyToken"));

			return result;
		}

		static string ReplaceCounting (string input, string pattern, string replacement, Action<int> onReplaced)
		{
			int count = Regex.Matches (input, pattern).Count;
			if (count == 0)
				return input;

			onReplaced (count);
			return Regex.Replace (input, pattern, replacement);
		}

		/// <summary>
		/// What each &lt;system.webServer&gt; child becomes, per PORTING-GUIDE.md step 4. Reported only -
		/// the tool never applies these, because none of them is a text substitution.
		/// </summary>
		public static string SystemWebServerAdvice (string child)
		{
			switch (child) {
			case "modules":
				return "move managed IHttpModule entries to <system.web><httpModules>; IIS-native ones become ASP.NET Core middleware";
			case "handlers":
				return "move to <system.web><httpHandlers>";
			case "rewrite":
				return "re-express with ASP.NET Core rewrite middleware, before UseWebForms () - there is no mechanical translation";
			case "staticContent":
			case "httpCompression":
				return "use UseStaticFiles () / UseResponseCompression (), before UseWebForms ()";
			case "defaultDocument":
				return "use UseDefaultFiles (), or route explicitly";
			case "httpErrors":
				return "use <system.web><customErrors>, or ASP.NET Core exception handling";
			case "security":
				return "request limits and filtering are Kestrel's or the reverse proxy's now";
			case "validation":
				return "an IIS-integration switch with no equivalent; delete it";
			default:
				return "IIS-specific; review against PORTING-GUIDE.md step 4";
			}
		}
	}
}
