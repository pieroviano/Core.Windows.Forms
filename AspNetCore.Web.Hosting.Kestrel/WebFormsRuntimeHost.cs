//
// One-time initialisation of the ported System.Web runtime inside a Kestrel process.
//
// On .NET Framework this was ApplicationHost.CreateApplicationHost: it built an AppDomain, stuffed
// the application's paths into it as AppDomain data, and let HttpRuntime read them back. There is
// one AppDomain here, so the same data is set on it directly - HttpRuntime.AppDomainAppPath and
// friends read AppDomain.GetData (".appPath") unmodified, which is why none of that code needed
// changing.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.Util;

namespace System.Web.Hosting.Kestrel
{
	public static class WebFormsRuntimeHost
	{
		static readonly object sync = new object ();
		static bool initialized;
		static string physical_path;
		static string virtual_path;
		static BareApplicationHost host;

		/// <summary>Physical directory of the application (where web.config lives).</summary>
		public static string PhysicalPath {
			get { return physical_path; }
		}

		/// <summary>Application's virtual path, always rooted and slash-terminated ("/" for a root app).</summary>
		public static string VirtualPath {
			get { return virtual_path; }
		}

		/// <summary>Path to the extracted machine.config this application is using.</summary>
		public static string MachineConfigPath {
			get { return PortPaths.MachineConfigPath; }
		}

		/// <summary>Path to the extracted root web.config (the default handler/module registrations).</summary>
		public static string RootWebConfigPath {
			get {
				string dir = Path.GetDirectoryName (PortPaths.MachineConfigPath);
				return dir == null ? null : Path.Combine (dir, "web.config");
			}
		}

		/// <summary>
		/// Serializer for state objects with no native encoding. See WebFormsOptions.StateSerializer.
		/// </summary>
		public static System.Web.IStateObjectSerializer StateSerializer {
			get { return StateSerializerAccessor.Get (); }
			set { StateSerializerAccessor.Set (value); }
		}

		public static bool IsInitialized {
			get { return Volatile.Read (ref initialized); }
		}

		/// <summary>
		/// A machine.config supplied by the host, used instead of the copy embedded in Core.Web.
		/// Must be set before <see cref="Initialize"/>. See WebFormsOptions.MachineConfigPath.
		/// </summary>
		public static string MachineConfigOverride { get; set; }

		/// <summary>
		/// Whether to assign the ASP.NET Core authentication result to System.Web's HttpContext.User.
		/// Must be set before <see cref="Initialize"/>. See WebFormsOptions.UseAspNetCoreAuthentication.
		/// </summary>
		public static bool UseAspNetCoreAuthentication { get; set; }

		/// <summary>
		/// Assemblies to load before the first request, so the types they contain can be found by
		/// name. Must be set before <see cref="Initialize"/>. See
		/// WebFormsOptions.ApplicationAssemblies.
		/// </summary>
		public static IEnumerable<Assembly> ApplicationAssemblies { get; set; }

		/// <summary>
		/// Directory for generated sources and compiled page assemblies. Must be set before
		/// <see cref="Initialize"/>. See WebFormsOptions.TemporaryFilesPath.
		/// </summary>
		public static string TemporaryFilesPath { get; set; }

		/// <summary>
		/// Prepares the runtime for an application rooted at <paramref name="physicalPath"/>.
		/// Idempotent; only the first call takes effect, because a process hosts one application.
		/// </summary>
		public static void Initialize (string physicalPath, string virtualPath = "/", string siteName = "Kestrel")
		{
			if (String.IsNullOrEmpty (physicalPath))
				throw new ArgumentException ("physicalPath is required", nameof (physicalPath));

			lock (sync) {
				if (initialized)
					return;

				physicalPath = Path.GetFullPath (physicalPath);
				if (!Directory.Exists (physicalPath))
					throw new DirectoryNotFoundException (
						"Application physical path does not exist: " + physicalPath);

				if (String.IsNullOrEmpty (virtualPath))
					virtualPath = "/";
				if (virtualPath [0] != '/')
					virtualPath = "/" + virtualPath;
				if (!virtualPath.EndsWith ("/", StringComparison.Ordinal))
					virtualPath += "/";

				physical_path = physicalPath;
				virtual_path = virtualPath;

				// Trailing separator matters: HttpRuntime.AppDomainAppPath is combined with
				// relative paths throughout the tree and upstream always had one.
				string appPathWithSlash = physicalPath.EndsWith (Path.DirectorySeparatorChar.ToString (), StringComparison.Ordinal)
					? physicalPath
					: physicalPath + Path.DirectorySeparatorChar;

				AppDomain domain = AppDomain.CurrentDomain;
				domain.SetData (".appPath", appPathWithSlash);
				domain.SetData (".appVPath", virtualPath);
				domain.SetData (".appId", siteName + virtualPath);
				domain.SetData (".domainId", siteName);
				domain.SetData (".hostingInstallDir", AppContext.BaseDirectory);
				// BuildManager and WebConfigurationHost probe this to decide they are running
				// hosted rather than in a design-time tool.
				domain.SetData (".:!MonoAspNetHostedApp!:.", "yes");

				// Replaces the AppDomainSetup properties that carry no meaning on .NET Core.
				PortPaths.ApplicationName = siteName;
				PortPaths.PrivateBinPath = Path.Combine (physicalPath, "bin");

				InstallAssemblyResolver (PortPaths.PrivateBinPath);
				PreloadApplicationAssemblies (PortPaths.PrivateBinPath);

				// Before the first configuration read, which is what would otherwise extract the
				// embedded copy. Validated here rather than at first use: a machine.config that does
				// not parse takes out EVERY section at once, and the resulting 500 says nothing about
				// the real cause.
				if (!String.IsNullOrEmpty (MachineConfigOverride)) {
					string machineConfig = Path.GetFullPath (MachineConfigOverride);

					if (!File.Exists (machineConfig))
						throw new FileNotFoundException (
							"WebFormsOptions.MachineConfigPath does not exist: " + machineConfig,
							machineConfig);

					try {
						new System.Xml.XmlDocument ().Load (machineConfig);
					} catch (System.Xml.XmlException e) {
						throw new InvalidOperationException (
							"WebFormsOptions.MachineConfigPath is not valid XML: " + machineConfig +
							". A malformed machine.config disables every configuration section at " +
							"once, so this is rejected at startup rather than at the first request.", e);
					}

					PortPaths.MachineConfigPath = machineConfig;
				}
				// Keyed on the application path by default, so a restart reuses what it compiled
				// last time. Two processes hosting the SAME directory therefore share this
				// directory and will corrupt each other's compilation output - hence the override.
				PortPaths.DynamicBase = String.IsNullOrEmpty (TemporaryFilesPath)
					? Path.Combine (Path.GetTempPath (), "aspnet-webforms",
							ApplicationKey (physicalPath), "assembly")
					: Path.GetFullPath (TemporaryFilesPath);
				Directory.CreateDirectory (PortPaths.DynamicBase);

				// Registered-object registry that HostingEnvironment delegates to. Creating it
				// also sets HostingEnvironment.Host, without which QueueBackgroundWorkItem throws.
				host = new BareApplicationHost (virtualPath, appPathWithSlash, PortPaths.DynamicBase);
				HostingEnvironment.IsHosted = true;
				HostingEnvironment.SiteName = siteName;

				// Installs HttpConfigurationSystem into System.Configuration so that web.config -
				// not a nonexistent app.config - answers ConfigurationManager.GetSection and
				// WebConfigurationManager.GetSection. Extracts the embedded machine.config as a
				// side effect of the first configuration read.
				WebConfigurationManager.Init ();

				// Point the *packaged* System.Configuration.ConfigurationManager at the same
				// configuration. Third-party libraries bind to its strong-named identity and nothing
				// this port can build satisfies that, so instead of replacing the assembly we tell its
				// ConfigurationManager where to read from. Without this, such a library silently sees
				// an empty configuration (an app.config that does not exist) rather than web.config.
				InstallPackagedConfigurationBridge ();

				// After WebConfigurationManager.Init: RegisterModule reads system.web/httpRuntime to
				// check allowDynamicModuleRegistration, so the configuration system has to be live.
				// The module is added to the dynamic list and instantiated when
				// HttpApplicationFactory builds the application on the first request.
				if (UseAspNetCoreAuthentication)
					HttpApplication.RegisterModule (typeof (AspNetCoreAuthenticationModule));

				initialized = true;
			}
		}

		/// <summary>
		/// The ASP.NET Core context for the request being served on this thread, or null outside a
		/// request (or when the application is not hosted by this package).
		/// </summary>
		/// <remarks>
		/// The bridge back to ASP.NET Core from inside WebForms code: authentication results,
		/// RequestServices for dependency injection, connection info, features. Reached through the
		/// standard IServiceProvider route that ASP.NET has always used to expose the worker request,
		/// so it costs nothing when unused.
		/// </remarks>
		public static Microsoft.AspNetCore.Http.HttpContext CurrentCoreContext {
			get {
				System.Web.HttpContext context = System.Web.HttpContext.Current;
				if (context == null)
					return null;

				var worker = ((IServiceProvider) context).GetService (typeof (HttpWorkerRequest))
					as AspNetCoreWorkerRequest;

				return worker?.CoreContext;
			}
		}

		/// <summary>
		/// Lets a type named in configuration or in an Inherits= directive resolve from the
		/// application's bin directory or from the host's own output directory.
		/// </summary>
		/// <remarks>
		/// Upstream, an application's assemblies lived in &lt;app&gt;/bin and the AppDomain's
		/// PrivateBinPath found them. That still works here, but the SDK builds into
		/// bin/Debug/net10.0 rather than bin/ - so whenever the application directory and the running
		/// assemblies are not the same place, a code-behind class named by Inherits= is simply not
		/// found and the page fails to compile.
		///
		/// This is safe in a way the AssemblyLoadContext hook tried for the ConfigurationManager
		/// identity problem was not (see build/WebFormsPort.targets). That one was rejected because
		/// it returned an assembly whose simple name differed from the request; this one only ever
		/// loads a file called exactly "&lt;requested simple name&gt;.dll", so the identity always
		/// matches.
		///
		/// The application's own bin is probed first, so a deployed copy always wins over the host's.
		/// </remarks>
		static void InstallAssemblyResolver (string privateBinPath)
		{
			string [] probePaths = { privateBinPath, AppContext.BaseDirectory };

			AssemblyLoadContext.Default.Resolving += (context, name) => {
				// Culture-specific satellite assemblies have their own resolution rules; do not
				// interfere with them.
				if (name.Name == null)
					return null;

				foreach (string directory in probePaths) {
					if (String.IsNullOrEmpty (directory) || !Directory.Exists (directory))
						continue;

					string candidate = Path.Combine (directory, name.Name + ".dll");
					if (!File.Exists (candidate))
						continue;

					try {
						return context.LoadFromAssemblyPath (Path.GetFullPath (candidate));
					} catch (BadImageFormatException) {
						// A native DLL sharing the name, or a corrupt file. Keep probing rather
						// than turning a missing-type error into a load error.
					} catch (FileLoadException) {
					}
				}

				return null;
			};
		}

		/// <summary>
		/// Loads the application's own assemblies, so types they declare are findable by name.
		/// </summary>
		/// <remarks>
		/// The assembly RESOLVER above is not enough on its own, and the distinction matters.
		///
		/// A reference like &lt;httpModules&gt; type="MyApp.Module, MyApp" names an assembly, so
		/// resolving it triggers a load and the resolver finds it. But Inherits="MyApp.DefaultPage"
		/// names no assembly, and HttpApplication.LoadType answers it by SCANNING ALREADY LOADED
		/// assemblies - no load is ever attempted, so no resolve event fires and the type is simply
		/// not found. The only fix for that case is to have the assembly loaded already.
		///
		/// Upstream got this for free: ASP.NET loaded every assembly in the application's bin
		/// directory at startup. That is what the bin probe below restores. The SDK's
		/// bin/Debug/net10.0 layout is not that directory though, so a host whose application assembly
		/// lives elsewhere names it through WebFormsOptions.ApplicationAssemblies - in a normal
		/// deployment the entry assembly IS the application, and is already loaded.
		/// </remarks>
		static void PreloadApplicationAssemblies (string privateBinPath)
		{
			IEnumerable<Assembly> declared = ApplicationAssemblies;
			if (declared != null) {
				// Touching the Assembly object is enough - it is loaded by definition. Enumerating
				// here rather than storing it keeps a lazy sequence from being evaluated later, when
				// the first request is already being served.
				foreach (Assembly assembly in declared) {
					if (assembly != null)
						AppDomain.CurrentDomain.Load (assembly.GetName ());
				}
			}

			if (String.IsNullOrEmpty (privateBinPath) || !Directory.Exists (privateBinPath))
				return;

			foreach (string dll in Directory.GetFiles (privateBinPath, "*.dll")) {
				try {
					AssemblyLoadContext.Default.LoadFromAssemblyPath (Path.GetFullPath (dll));
				} catch (BadImageFormatException) {
					// Native DLLs routinely sit in bin next to managed ones.
				} catch (FileLoadException) {
					// Already loaded from somewhere else; that is the outcome we wanted anyway.
				}
			}
		}

		/// <summary>
		/// Stops registered objects. Wire to IHostApplicationLifetime.ApplicationStopping - it
		/// replaces the AppDomain.DomainUnload handling upstream relied on.
		/// </summary>
		public static void Shutdown ()
		{
			lock (sync) {
				if (!initialized)
					return;
				BareApplicationHost h = host;
				if (h != null)
					h.StopAll ();
			}
		}

		/// <summary>Maps an application-relative or rooted virtual path to a physical path.</summary>
		public static string MapPath (string virtualPath)
		{
			EnsureInitialized ();

			if (String.IsNullOrEmpty (virtualPath))
				return physical_path;

			string path = virtualPath.Replace ('\\', '/');

			if (path.StartsWith ("~/", StringComparison.Ordinal))
				path = path.Substring (2);
			else if (path == "~")
				path = String.Empty;
			else if (path [0] == '/') {
				// Strip the application's virtual path prefix; what remains is app-relative.
				if (virtual_path.Length > 1 &&
				    path.StartsWith (virtual_path.TrimEnd ('/'), StringComparison.OrdinalIgnoreCase))
					path = path.Substring (virtual_path.TrimEnd ('/').Length);
				path = path.TrimStart ('/');
			}

			if (path.Length == 0)
				return physical_path;

			string mapped = Path.GetFullPath (Path.Combine (physical_path, path.Replace ('/', Path.DirectorySeparatorChar)));

			// Refuse to escape the application root: a request path must never map outside it.
			if (!mapped.StartsWith (physical_path, StringComparison.OrdinalIgnoreCase))
				throw new HttpException (403, "Path is outside the application root: " + virtualPath);

			return mapped;
		}

		// Bridges the two System.Configuration implementations. Everything crossing the boundary is a
		// primitive or a shared framework type, so the packaged types and the ported ones never meet.
		static void InstallPackagedConfigurationBridge ()
		{
			System.Web.Configuration.Bridge.PackagedConfigurationBridge.Install (
				appSettings: () => WebConfigurationManager.AppSettings,
				connectionStrings: () => {
					var list = new System.Collections.Generic.List<
						System.Web.Configuration.Bridge.BridgedConnectionString> ();
					var settings = WebConfigurationManager.ConnectionStrings;
					if (settings != null) {
						foreach (System.Configuration.ConnectionStringSettings cs in settings) {
							if (cs != null)
								list.Add (new System.Web.Configuration.Bridge.BridgedConnectionString (
									cs.Name, cs.ConnectionString, cs.ProviderName));
						}
					}
					return list;
				});
		}

		static void EnsureInitialized ()
		{
			if (!Volatile.Read (ref initialized))
				throw new InvalidOperationException (
					"The WebForms runtime is not initialised. Call app.UseWebForms (...) or " +
					"WebFormsRuntimeHost.Initialize (physicalPath) during startup.");
		}

		// Stable per-application discriminator so two applications on one machine never share a
		// codegen or config directory.
		static string ApplicationKey (string physicalPath)
		{
			using (var sha = System.Security.Cryptography.SHA256.Create ()) {
				byte [] hash = sha.ComputeHash (
					System.Text.Encoding.UTF8.GetBytes (physicalPath.ToLowerInvariant ()));
				var sb = new System.Text.StringBuilder (16);
				for (int i = 0; i < 8; i++)
					sb.Append (hash [i].ToString ("x2"));
				return sb.ToString ();
			}
		}
	}
}
