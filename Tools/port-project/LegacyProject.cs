//
// Reading the legacy .csproj / .vbproj.
//
// XDocument, not the MSBuild API. That decision and its measured justification are in
// PORT-TOOL-PLAN.md 0.4: MSBuild can in fact evaluate a ToolsVersion="4.0" project, so the reason is
// not "it cannot load" - it is that pulling in Microsoft.Build + MSBuildLocator to read literal XML
// elements is disproportionate, and that evaluation results would vary with whatever Visual Studio
// has installed on the machine doing the conversion. Reading the document as written is reproducible.
//
// The cost is accepted and reported rather than hidden: a <Reference> inside a Condition'ed ItemGroup,
// or one whose Include comes from a property, is read exactly as written (see Planner's
// ConditionedItemGroups finding).
//
// Everything here matches on LocalName. A legacy project carries
// xmlns="http://schemas.microsoft.com/developer/msbuild/2003" and an SDK-style one carries no
// namespace at all, and the tool has to read both - the second only to recognise it and refuse.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PortProject
{
	enum Language
	{
		CSharp,
		VisualBasic,
	}

	sealed class LegacyProject
	{
		LegacyProject (string path, XDocument document)
		{
			Path = path;
			Document = document;
			Directory = System.IO.Path.GetDirectoryName (path);
			Language = path.EndsWith (".vbproj", StringComparison.OrdinalIgnoreCase)
				? Language.VisualBasic
				: Language.CSharp;
		}

		public string Path { get; }

		public string Directory { get; }

		public XDocument Document { get; }

		public Language Language { get; }

		public string FileName {
			get { return System.IO.Path.GetFileName (Path); }
		}

		/// <summary>An SDK-style project. The tool refuses these - see Planner.</summary>
		public bool IsSdkStyle {
			get { return Document.Root != null && Document.Root.Attribute ("Sdk") != null; }
		}

		public string AssemblyName {
			get { return Property ("AssemblyName") ?? System.IO.Path.GetFileNameWithoutExtension (Path); }
		}

		public string RootNamespace {
			get { return Property ("RootNamespace"); }
		}

		public IReadOnlyList<string> ProjectTypeGuids {
			get {
				string value = Property ("ProjectTypeGuids");
				return value == null
					? Array.Empty<string> ()
					: value.Split (';').Select (g => g.Trim ()).Where (g => g.Length > 0).ToArray ();
			}
		}

		/// <summary>Assembly names from &lt;Reference Include="System.Web.Mvc, Version=..." /&gt;.</summary>
		public IReadOnlyList<Reference> References {
			get {
				return Elements ("Reference")
					.Select (e => new Reference (
						// The Include is an assembly display name; only the simple name matters here.
						(e.Attribute ("Include")?.Value ?? String.Empty).Split (',') [0].Trim (),
						LineOf (e)))
					.Where (r => r.Name.Length > 0)
					.ToArray ();
			}
		}

		public IReadOnlyList<string> ProjectReferences {
			get {
				return Elements ("ProjectReference")
					.Select (e => e.Attribute ("Include")?.Value)
					.Where (v => !String.IsNullOrEmpty (v))
					.ToArray ();
			}
		}

		/// <summary>
		/// NuGet packages, from either &lt;PackageReference&gt; or a packages.config beside the project.
		/// A 2013-era web project keeps them in packages.config, which is where Microsoft.AspNet.Mvc
		/// and Microsoft.AspNet.Web.Optimization will actually be found.
		/// </summary>
		public IReadOnlyList<PackageReference> PackageReferences { get; private set; } = Array.Empty<PackageReference> ();

		/// <summary>True when any ItemGroup or Reference carries a Condition we did not evaluate.</summary>
		public bool HasConditionedItems {
			get {
				return Document.Descendants ()
					.Where (e => e.Name.LocalName == "ItemGroup" || e.Name.LocalName == "Reference")
					.Any (e => e.Attribute ("Condition") != null);
			}
		}

		public static LegacyProject Load (string path)
		{
			var project = new LegacyProject (path, XDocument.Load (path, LoadOptions.SetLineInfo));
			project.PackageReferences = ReadPackages (project);
			return project;
		}

		static IReadOnlyList<PackageReference> ReadPackages (LegacyProject project)
		{
			var packages = new List<PackageReference> ();

			foreach (XElement element in project.Elements ("PackageReference")) {
				string id = element.Attribute ("Include")?.Value;
				if (!String.IsNullOrEmpty (id))
					packages.Add (new PackageReference (
						id,
						element.Attribute ("Version")?.Value,
						project.FileName + ":" + LineOf (element)));
			}

			string packagesConfig = System.IO.Path.Combine (project.Directory, "packages.config");
			if (File.Exists (packagesConfig)) {
				XDocument document;
				try {
					document = XDocument.Load (packagesConfig, LoadOptions.SetLineInfo);
				} catch (Exception) {
					// A malformed packages.config must not take the whole conversion down; the
					// planner reports the packages it did find and the user still gets a project.
					return packages;
				}

				foreach (XElement element in document.Descendants ().Where (e => e.Name.LocalName == "package")) {
					string id = element.Attribute ("id")?.Value;
					if (!String.IsNullOrEmpty (id))
						packages.Add (new PackageReference (
							id,
							element.Attribute ("version")?.Value,
							"packages.config:" + LineOf (element)));
				}
			}

			return packages;
		}

		IEnumerable<XElement> Elements (string localName)
		{
			return Document.Descendants ().Where (e => e.Name.LocalName == localName);
		}

		string Property (string localName)
		{
			// Last one wins, which is MSBuild's own rule for repeated property definitions.
			string value = Elements (localName)
				.Where (e => e.Parent != null && e.Parent.Name.LocalName == "PropertyGroup")
				.Select (e => e.Value.Trim ())
				.LastOrDefault ();

			return String.IsNullOrEmpty (value) ? null : value;
		}

		static int LineOf (XElement element)
		{
			var info = (System.Xml.IXmlLineInfo) element;
			return info.HasLineInfo () ? info.LineNumber : 0;
		}
	}

	sealed class Reference
	{
		public Reference (string name, int line)
		{
			Name = name;
			Line = line;
		}

		public string Name { get; }

		public int Line { get; }
	}

	sealed class PackageReference
	{
		public PackageReference (string id, string version, string evidence)
		{
			Id = id;
			Version = version;
			Evidence = evidence;
		}

		public string Id { get; }

		public string Version { get; }

		/// <summary>"MyApp.csproj:41" or "packages.config:3".</summary>
		public string Evidence { get; }
	}
}
