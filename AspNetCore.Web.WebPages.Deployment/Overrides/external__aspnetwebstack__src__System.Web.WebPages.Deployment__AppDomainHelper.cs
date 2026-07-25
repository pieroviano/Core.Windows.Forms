//
// System.Web.WebPages.Deployment.AppDomainHelper - port override
//
// Upstream: mono/external/aspnetwebstack/src/System.Web.WebPages.Deployment/AppDomainHelper.cs
//
// Upstream creates a CHILD APPDOMAIN to inspect the assemblies in an application's bin directory
// without loading them into the main one, marshals a RemoteAssemblyLoader across it, and unloads the
// domain afterwards. Every part of that is gone on CoreCLR: AppDomain.CreateDomain,
// AppDomainSetup.ApplicationBase/ConfigurationFile/PrivateBinPath, AppDomain.Evidence,
// CreateInstanceAndUnwrap, MarshalByRefObject and AppDomain.Unload.
//
// A collectible AssemblyLoadContext is the direct equivalent, and expresses the same intent: load in
// isolation, read what is needed, discard without leaving the assemblies behind. It is used here
// rather than a plain Assembly.LoadFrom precisely because this must NOT permanently load an
// application's bin into the running process - upstream went to the trouble of a separate domain for
// that reason, and the port should not quietly drop the isolation.
//
// The single caller is WebPagesDeployment.GetIncompatibleDependencies, an [EditorBrowsable (Never)]
// diagnostic that WebMatrix used to warn about side-by-side WebPages versions. It is not on the
// request path, so this is about keeping the API honest rather than about serving pages.
//

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace System.Web.WebPages.Deployment
{
	internal static class AppDomainHelper
	{
		public static IDictionary<string, IEnumerable<string>> GetBinAssemblyReferences (string appPath, string configPath)
		{
			string binDirectory = Path.Combine (appPath, "bin");
			if (!Directory.Exists (binDirectory))
				return null;

			// configPath is unused here. Upstream passed it as the child domain's ConfigurationFile so
			// that binding redirects applied while it resolved references; there is no child domain
			// and no such configuration to apply, and the references are read from metadata rather
			// than resolved, so nothing needs it.
			return Inspect (binDirectory);
		}

		// Kept out of the caller so the collectible context is unreachable the moment it returns,
		// which is what lets it actually unload.
		[MethodImpl (MethodImplOptions.NoInlining)]
		static IDictionary<string, IEnumerable<string>> Inspect (string binDirectory)
		{
			var context = new AssemblyLoadContext ("WebPagesDeploymentInspection", isCollectible: true);

			try {
				var references = new Dictionary<string, IEnumerable<string>> ();

				foreach (string assemblyPath in Directory.EnumerateFiles (binDirectory, "*.dll")) {
					try {
						Assembly assembly = context.LoadFromAssemblyPath (Path.GetFullPath (assemblyPath));

						// Materialised into an array before the context is unloaded: these are plain
						// strings, so nothing survives that would keep the context alive.
						references [assemblyPath] = assembly.GetReferencedAssemblies ()
							.Select (name => name.FullName)
							.Concat (new [] { assembly.FullName })
							.ToArray ();
					} catch (BadImageFormatException) {
						// A native DLL sitting in bin. Upstream would have thrown out of the child
						// domain here; skipping it is strictly better and matches what the caller -
						// a diagnostic - can do anything useful with.
					} catch (FileLoadException) {
					}
				}

				return references;
			} finally {
				context.Unload ();
			}
		}
	}
}
