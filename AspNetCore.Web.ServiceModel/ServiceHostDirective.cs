//
// Reads the @ServiceHost directive out of a .svc file.
//
// A .svc file is a single directive and nothing else:
//
//     <%@ ServiceHost Language="C#" Debug="true" Service="MyApp.Services.EchoService" %>
//
// Only Service matters for hosting; the rest described how ASP.NET should compile a code-behind that
// no longer exists here. Factory is recognised so an application that names a custom
// ServiceHostFactory gets a clear error rather than being silently served with the default one.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace System.Web.ServiceModel
{
	/// <summary>The contents of a <c>.svc</c> file's <c>@ServiceHost</c> directive.</summary>
	public sealed class ServiceHostDirective
	{
		static readonly Regex DirectivePattern = new Regex (
			@"<%@\s*ServiceHost\b(?<attributes>[^%]*)%>",
			RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

		static readonly Regex AttributePattern = new Regex (
			@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>[^""]*)""",
			RegexOptions.Compiled);

		ServiceHostDirective (string virtualPath, string physicalPath, IDictionary<string, string> attributes)
		{
			VirtualPath = virtualPath;
			PhysicalPath = physicalPath;
			Attributes = attributes;
		}

		/// <summary>Application-relative URL the file is served at, e.g. <c>/Echo.svc</c>.</summary>
		public string VirtualPath { get; }

		public string PhysicalPath { get; }

		/// <summary>Every attribute on the directive, keyed case-insensitively.</summary>
		public IDictionary<string, string> Attributes { get; }

		/// <summary>The <c>Service</c> attribute - the service type's name.</summary>
		public string ServiceTypeName {
			get { return Attributes.TryGetValue ("Service", out string value) ? value : null; }
		}

		/// <summary>The <c>Factory</c> attribute, if the application named a custom host factory.</summary>
		public string FactoryTypeName {
			get { return Attributes.TryGetValue ("Factory", out string value) ? value : null; }
		}

		/// <summary>
		/// Parses one <c>.svc</c> file. Returns null when the file carries no <c>@ServiceHost</c>
		/// directive at all, which is not an error - it simply is not a service.
		/// </summary>
		public static ServiceHostDirective Parse (string physicalPath, string virtualPath)
		{
			if (physicalPath == null)
				throw new ArgumentNullException (nameof (physicalPath));

			Match match = DirectivePattern.Match (File.ReadAllText (physicalPath));
			if (!match.Success)
				return null;

			var attributes = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
			foreach (Match attribute in AttributePattern.Matches (match.Groups ["attributes"].Value))
				attributes [attribute.Groups ["name"].Value] = attribute.Groups ["value"].Value.Trim ();

			return new ServiceHostDirective (virtualPath, physicalPath, attributes);
		}

		/// <summary>
		/// Every <c>.svc</c> under an application directory that carries a directive, with the virtual
		/// path each is reached at.
		/// </summary>
		public static IEnumerable<ServiceHostDirective> Discover (string applicationPhysicalPath)
		{
			if (String.IsNullOrEmpty (applicationPhysicalPath) || !Directory.Exists (applicationPhysicalPath))
				yield break;

			string root = Path.GetFullPath (applicationPhysicalPath);

			foreach (string file in Directory.EnumerateFiles (root, "*.svc", SearchOption.AllDirectories)) {
				// bin/ and obj/ hold build output, including a copy of the site once it has been
				// published into them. Serving those copies would register every service twice.
				string relative = Path.GetRelativePath (root, file);
				if (relative.StartsWith ("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
				    relative.StartsWith ("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					continue;

				ServiceHostDirective directive = Parse (file, "/" + relative.Replace (Path.DirectorySeparatorChar, '/'));
				if (directive != null)
					yield return directive;
			}
		}
	}
}
