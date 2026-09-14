//
// System.Web.Hosting.ApplicationManager - port override
//
// Upstream: mono/mcs/class/System.Web/System.Web.Hosting/ApplicationManager.cs
//
// Same public surface and the same rules - one domain per application id, created on first use; one
// registered object per type in each; CreateObject fails or returns the existing object according to
// failIfExists. What had to change is what an application IS:
//
//   upstream   a BareApplicationHost created in a new AppDomain through CreateApplicationHost
//   here       a child process (ApplicationDomains.Start) plus an ApplicationObjectHost inside it
//
// BareApplicationHost could not simply be created remotely: it is internal and sealed, and a remoting
// proxy can only stand in for a public, overridable contract - hence IApplicationObjectHost. Objects are
// named across the boundary by assembly-qualified type name, since a Type cannot cross it.
//
// Upstream's IsIdle is a MonoTODO that throws, and stays that way.
//

using System;
using System.Collections.Generic;
using System.Runtime.Remoting;
using System.Security.Permissions;
using System.Threading;

namespace System.Web.Hosting
{
	[AspNetHostingPermission (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	public sealed class ApplicationManager : MarshalByRefObject
	{
		static readonly ApplicationManager instance = new ApplicationManager ();

		readonly object sync = new object ();
		readonly Dictionary<string, Application> applications = new Dictionary<string, Application> ();
		int users;

		sealed class Application
		{
			public string Id;
			public string VirtualPath;
			public string PhysicalPath;
			public ChildAppDomain Domain;
			public IApplicationObjectHost Objects;
		}

		ApplicationManager ()
		{
		}

		public static ApplicationManager GetApplicationManager ()
		{
			return instance;
		}

		public void Open ()
		{
			Interlocked.Increment (ref users);
		}

		public void Close ()
		{
			if (Interlocked.Decrement (ref users) == 0)
				ShutdownAll ();
		}

		public IRegisteredObject CreateObject (IApplicationHost appHost, Type type)
		{
			if (appHost == null)
				throw new ArgumentNullException ("appHost");
			if (type == null)
				throw new ArgumentNullException ("type");

			return CreateObject (appHost.GetSiteID (), type, appHost.GetVirtualPath (), appHost.GetPhysicalPath (), true, true);
		}

		public IRegisteredObject CreateObject (string appId, Type type, string virtualPath, string physicalPath, bool failIfExists)
		{
			return CreateObject (appId, type, virtualPath, physicalPath, failIfExists, true);
		}

		public IRegisteredObject CreateObject (string appId, Type type, string virtualPath, string physicalPath,
						       bool failIfExists, bool throwOnError)
		{
			if (appId == null)
				throw new ArgumentNullException ("appId");
			if (!VirtualPathUtility.IsAbsolute (virtualPath))
				throw new ArgumentException ("Relative path no allowed.", "virtualPath");
			if (String.IsNullOrEmpty (physicalPath))
				throw new ArgumentException ("Cannot be null or empty", "physicalPath");
			if (!typeof (IRegisteredObject).IsAssignableFrom (type))
				throw new ArgumentException (String.Concat ("Type '", type.Name, "' does not implement IRegisteredObject."), "type");

			RemotingConfiguration.AllowAssembly (type.Assembly);
			string typeName = type.AssemblyQualifiedName;

			try {
				Application application = GetOrStart (appId, virtualPath, physicalPath);

				IRegisteredObject existing = application.Objects.GetObject (typeName);
				if (existing != null) {
					if (failIfExists)
						throw new InvalidOperationException (String.Concat ("Well known object of type '", type.Name, "' already exists in this domain."));
					return existing;
				}

				return application.Objects.CreateObject (typeName);
			} catch (Exception) {
				if (throwOnError)
					throw;
				return null;
			}
		}

		public IRegisteredObject GetObject (string appId, Type type)
		{
			if (appId == null)
				throw new ArgumentNullException ("appId");
			if (type == null)
				throw new ArgumentNullException ("type");

			Application application = Find (appId);
			return application == null ? null : application.Objects.GetObject (type.AssemblyQualifiedName);
		}

		public ApplicationInfo [] GetRunningApplications ()
		{
			lock (sync) {
				var result = new List<ApplicationInfo> (applications.Count);
				foreach (Application application in applications.Values) {
					if (application.Domain.IsAlive)
						result.Add (new ApplicationInfo (application.Id, application.PhysicalPath, application.VirtualPath));
				}
				return result.ToArray ();
			}
		}

		public override object InitializeLifetimeService ()
		{
			return null;
		}

		public bool IsIdle ()
		{
			throw new NotImplementedException ();
		}

		public void ShutdownAll ()
		{
			Application [] all;
			lock (sync) {
				all = new Application [applications.Count];
				applications.Values.CopyTo (all, 0);
				applications.Clear ();
			}

			foreach (Application application in all)
				application.Domain.Unload ();
		}

		public void ShutdownApplication (string appId)
		{
			if (appId == null)
				throw new ArgumentNullException ("appId");

			Application application;
			lock (sync) {
				if (!applications.TryGetValue (appId, out application))
					return;
				applications.Remove (appId);
			}

			application.Domain.Unload ();
		}

		public void StopObject (string appId, Type type)
		{
			if (appId == null)
				throw new ArgumentNullException ("appId");
			if (type == null)
				throw new ArgumentNullException ("type");

			Application application = Find (appId);
			if (application != null)
				application.Objects.StopObject (type.AssemblyQualifiedName);
		}

		Application Find (string appId)
		{
			lock (sync) {
				Application application;
				return applications.TryGetValue (appId, out application) && application.Domain.IsAlive ? application : null;
			}
		}

		// Starting a domain takes a noticeable fraction of a second, so it happens under the lock only for the
		// id being started - which is what upstream's single dictionary guaranteed, one domain per id.
		Application GetOrStart (string appId, string virtualPath, string physicalPath)
		{
			lock (sync) {
				Application application;
				if (applications.TryGetValue (appId, out application)) {
					if (application.Domain.IsAlive)
						return application;
					applications.Remove (appId);
				}

				ApplicationDomainSettings settings = ApplicationDomainSettings.FromCurrentRuntime (virtualPath, physicalPath);
				settings.SiteName = appId;
				var events = new ShutdownOnRequest (this, appId);
				ChildAppDomain domain = ApplicationDomains.Start (settings, events, TimeSpan.FromMinutes (2));

				application = new Application {
					Id = appId,
					VirtualPath = settings.VirtualPath,
					PhysicalPath = settings.PhysicalPath,
					Domain = domain,
					Objects = domain.CreateInstanceAndUnwrap<ApplicationObjectHost> (),
				};
				applications [appId] = application;
				return application;
			}
		}

		/// <summary>An application asking to unload is shut down, as its AppDomain unloading removed it upstream.</summary>
		sealed class ShutdownOnRequest : MarshalByRefObject, IApplicationDomainEvents
		{
			readonly ApplicationManager manager;
			readonly string appId;

			public ShutdownOnRequest (ApplicationManager manager, string appId)
			{
				this.manager = manager;
				this.appId = appId;
			}

			public void UnloadRequested (string reason)
			{
				ThreadPool.QueueUserWorkItem (_ => manager.ShutdownApplication (appId));
			}
		}
	}
}
