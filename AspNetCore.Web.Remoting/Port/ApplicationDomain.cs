//
// An ASP.NET application domain, which on .NET Core is a child process.
//
// On .NET Framework ApplicationHost.CreateApplicationHost built an AppDomain, set .appPath, .appVPath
// and friends on it, and HttpRuntime read them back. Here Net4x.AppDomain starts a child process, and
// ApplicationDomainInitializer - created inside it - runs the same WebFormsRuntimeHost.Initialize a
// Kestrel application runs, so HttpRuntime inside the child sees exactly what it would in its own
// process. Everything the host later creates in the domain (a host object, a registered object, a
// request worker) is a remoting proxy to an object living there.
//
// The domain's ApplicationBase is the PARENT's output directory: that is where Core.Web and the rest of
// the port are deployed, and a child process cannot load them from anywhere else. The application's
// own bin is its PrivateBinPath, as it was for an AppDomain, and anything else the parent has loaded is
// offered to the child by path when it asks.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Threading.Tasks;
using System.Web.Hosting.Kestrel;
using System.Web.SessionState;

namespace System.Web.Hosting
{
	/// <summary>What a child application domain is initialised with. Crosses the process boundary.</summary>
	[Serializable]
	public sealed class ApplicationDomainSettings
	{
		public string PhysicalPath;
		public string VirtualPath = "/";
		public string SiteName = "Kestrel";

		/// <summary>Compilation output directory; null for the default keyed on the physical path.</summary>
		public string TemporaryFilesPath;

		/// <summary>A complete machine.config to use instead of the embedded one; null for the default.</summary>
		public string MachineConfigPath;

		/// <summary>Assemblies to load before the first request, by path. See WebFormsOptions.ApplicationAssemblies.</summary>
		public string [] ApplicationAssemblyPaths = new string [0];

		/// <summary>Assembly-qualified IStateObjectSerializer type with a parameterless constructor, or null.</summary>
		public string StateSerializerType;

		/// <summary>The store for mode="StateServer"; see PortSessionState.</summary>
		public string StateServerProviderType;

		/// <summary>Settings inherited from the runtime configured in this process, for a domain it creates.</summary>
		internal static ApplicationDomainSettings FromCurrentRuntime (string virtualPath, string physicalPath)
		{
			var settings = new ApplicationDomainSettings {
				PhysicalPath = Path.GetFullPath (physicalPath),
				VirtualPath = String.IsNullOrEmpty (virtualPath) ? "/" : virtualPath,
				MachineConfigPath = WebFormsRuntimeHost.MachineConfigOverride,
				StateServerProviderType = PortSessionState.StateServerProviderType,
			};

			IStateObjectSerializer serializer = WebFormsRuntimeHost.StateSerializer;
			if (serializer != null)
				settings.StateSerializerType = serializer.GetType ().AssemblyQualifiedName;

			IEnumerable<Assembly> assemblies = WebFormsRuntimeHost.ApplicationAssemblies;
			if (assemblies != null)
				settings.ApplicationAssemblyPaths = assemblies
					.Where (a => a != null && !a.IsDynamic && a.Location.Length > 0)
					.Select (a => a.Location)
					.ToArray ();

			return settings;
		}
	}

	/// <summary>What a child application domain tells the process that owns it.</summary>
	public interface IApplicationDomainEvents
	{
		/// <summary>
		/// HttpRuntime.UnloadAppDomain ran in the domain - a file watcher saw bin, App_Code, Global.asax or
		/// web.config change, or application code asked. The domain refuses new requests from here on.
		/// </summary>
		void UnloadRequested (string reason);
	}

	/// <summary>Created inside a child domain to bring HttpRuntime up for the application.</summary>
	public class ApplicationDomainInitializer : MarshalByRefObject
	{
		public virtual int ProcessId => Environment.ProcessId;

		public virtual void Initialize (ApplicationDomainSettings settings, IApplicationDomainEvents events)
		{
			if (settings == null)
				throw new ArgumentNullException (nameof (settings));

			var assemblies = new List<Assembly> ();
			foreach (string path in settings.ApplicationAssemblyPaths ?? new string [0])
				assemblies.Add (Assembly.LoadFrom (path));

			WebFormsRuntimeHost.ApplicationAssemblies = assemblies;
			WebFormsRuntimeHost.MachineConfigOverride = settings.MachineConfigPath;
			WebFormsRuntimeHost.TemporaryFilesPath = settings.TemporaryFilesPath;
			if (settings.StateServerProviderType != null)
				PortSessionState.StateServerProviderType = settings.StateServerProviderType;

			WebFormsRuntimeHost.Initialize (settings.PhysicalPath, settings.VirtualPath, settings.SiteName);

			if (settings.StateSerializerType != null)
				WebFormsRuntimeHost.StateSerializer = (IStateObjectSerializer) Activator.CreateInstance (
					Type.GetType (settings.StateSerializerType, throwOnError: true));

			// An AppDomain unload stopped registered objects; ending the process is this domain's unload.
			AppDomain.CurrentDomain.ProcessExit += (sender, e) => WebFormsRuntimeHost.Shutdown ();

			if (events != null) {
				PortApplicationLifetime.UnloadRequested = reason => {
					// Off the calling thread - a file watcher, or a request - and never allowed to fail it:
					// the owner may unload this process while the notification is still in flight.
					Task.Run (() => {
						try {
							events.UnloadRequested (reason.ToString ());
						} catch (Exception) {
						}
					});
				};
			}
		}
	}

	/// <summary>The registered-object side of a child domain, for ApplicationManager.</summary>
	public interface IApplicationObjectHost
	{
		IRegisteredObject CreateObject (string assemblyQualifiedTypeName);

		IRegisteredObject GetObject (string assemblyQualifiedTypeName);

		void StopObject (string assemblyQualifiedTypeName);
	}

	public class ApplicationObjectHost : MarshalByRefObject, IApplicationObjectHost
	{
		public virtual IRegisteredObject CreateObject (string assemblyQualifiedTypeName)
		{
			Type type = Resolve (assemblyQualifiedTypeName);
			if (!typeof (MarshalByRefObject).IsAssignableFrom (type))
				throw new ArgumentException (
					"Type '" + type.FullName + "' must derive from MarshalByRefObject: it lives in the application's " +
					"domain, a separate process, and is used through a remoting proxy.");

			BareApplicationHost host = Host;
			IRegisteredObject created = host.CreateInstance (type);
			// Upstream registers the object unless its constructor already did.
			if (host.GetObject (type) == null)
				host.RegisterObject (created, true);
			return created;
		}

		public virtual IRegisteredObject GetObject (string assemblyQualifiedTypeName)
			=> Host.GetObject (Resolve (assemblyQualifiedTypeName));

		public virtual void StopObject (string assemblyQualifiedTypeName)
			=> Host.StopObject (Resolve (assemblyQualifiedTypeName));

		static BareApplicationHost Host => HostingEnvironment.Host
			?? throw new InvalidOperationException ("The application domain has not been initialised.");

		static Type Resolve (string name) => Type.GetType (name, throwOnError: true);
	}

	/// <summary>Starts child application domains.</summary>
	internal static class ApplicationDomains
	{
		/// <summary>
		/// Starts a domain for <paramref name="settings"/> and initialises the runtime in it. The domain is
		/// unloaded again if initialisation fails.
		/// </summary>
		internal static ChildAppDomain Start (ApplicationDomainSettings settings, IApplicationDomainEvents events,
						      TimeSpan callTimeout)
		{
			if (String.IsNullOrEmpty (settings.PhysicalPath) || !Directory.Exists (settings.PhysicalPath))
				throw new DirectoryNotFoundException (
					"The application's physical path does not exist: " + settings.PhysicalPath);

			var setup = new AppDomainSetup2 {
				ApplicationBase = AppContext.BaseDirectory,
				PrivateBinPath = Path.Combine (settings.PhysicalPath, "bin"),
				ApplicationName = settings.SiteName + settings.VirtualPath,
				StartupTimeout = TimeSpan.FromSeconds (60),
				CallTimeout = callTimeout,
			};

			string name = "ASP.NET " + settings.SiteName + settings.VirtualPath;
			ChildAppDomain domain = AppDomain.CurrentDomain.CreateChildDomain (name, setup);
			domain.AssemblyResolve += ResolveFromThisProcess;

			try {
				// The application's types and the port's own cross in both directions.
				RemotingConfiguration.AllowAssembly (typeof (ApplicationDomainInitializer).Assembly);
				RemotingConfiguration.AllowAssembly (typeof (HttpRuntime).Assembly);

				var initializer = domain.CreateInstanceAndUnwrap<ApplicationDomainInitializer> ();
				initializer.Initialize (settings, events);
				return domain;
			} catch {
				domain.Unload ();
				throw;
			}
		}

		// The child probes the parent's output directory and the application's bin first; this answers for
		// anything else this process has loaded - typically an assembly the host loaded from elsewhere.
		static AssemblyResolveResponse ResolveFromThisProcess (ChildAppDomain domain, AssemblyResolveRequest request)
		{
			string simpleName = request.AssemblyName.Name;
			foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies ()) {
				if (loaded.IsDynamic || loaded.Location.Length == 0)
					continue;
				if (String.Equals (loaded.GetName ().Name, simpleName, StringComparison.OrdinalIgnoreCase))
					return AssemblyResolveResponse.FromPath (loaded.Location);
			}
			return null;
		}
	}
}
