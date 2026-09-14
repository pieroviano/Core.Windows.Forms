//
// System.Web.Hosting.ApplicationHost - port override
//
// Upstream: mono/mcs/class/System.Web/System.Web.Hosting/ApplicationHost.cs
//
// CreateApplicationHost() built a second AppDomain and marshalled the host into it. There is
// one AppDomain on .NET Core; CreateApplicationHost delegates to Core.Web.Remoting, which uses a child
// process instead (see below). The rest of the tree consumes two other members:
//
//   MonoHostedDataKey  - BuildManager and WebConfigurationHost probe AppDomain data under this
//                        key to decide whether they are running hosted. WebFormsRuntimeHost sets
//                        it during initialisation, so those checks keep working unchanged.
//   FindWebConfig      - case-insensitive web.config lookup, verbatim from upstream.
//
// Dropped: ClearDynamicBaseDirectory, CreateDirectory, BuildPrivateBinPath and
// SetHostingEnvironment. All four were reachable only from CreateApplicationHost.
//

using System;
using System.IO;
using System.Security.Permissions;

namespace System.Web.Hosting
{
	// CAS
	[AspNetHostingPermission (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	public sealed class ApplicationHost
	{
		internal const string MonoHostedDataKey = ".:!MonoAspNetHostedApp!:.";

		ApplicationHost ()
		{
		}

		internal static string FindWebConfig (string basedir)
		{
			if (String.IsNullOrEmpty (basedir) || !Directory.Exists (basedir))
				return null;

			string [] files = Directory.GetFileSystemEntries (basedir, "?eb.?onfig");
			if (files == null || files.Length == 0)
				return null;
			return files [0];
		}

		// A second AppDomain cannot exist on .NET Core, but a child PROCESS can play its part:
		// Core.Web.Remoting starts one, initialises the runtime in it for the application, and returns a
		// remoting proxy to the host object. It is found by name so Core.Web takes no remoting
		// dependency; without it the call fails naming the package to add.
		const string FactoryTypeName = "System.Web.Hosting.RemotingApplicationHostFactory, Core.Web.Remoting";

		public static object CreateApplicationHost (Type hostType, string virtualDir, string physicalDir)
		{
			if (hostType == null)
				throw new ArgumentNullException ("hostType");
			if (!typeof (MarshalByRefObject).IsAssignableFrom (hostType))
				throw new ArgumentException ("hostType must derive from MarshalByRefObject.", "hostType");

			Type factoryType = Type.GetType (FactoryTypeName, throwOnError: false);
			if (factoryType == null)
				throw new PlatformNotSupportedException (
					"ApplicationHost.CreateApplicationHost needs Core.Web.Remoting: an application domain is " +
					"a child process on .NET Core, and that assembly provides it. Reference the " +
					"Core.AspNet.Web.Remoting package.");

			var factory = (IApplicationHostFactory) Activator.CreateInstance (factoryType, nonPublic: true);
			return factory.CreateApplicationHost (hostType, virtualDir, physicalDir);
		}
	}

	/// <summary>Implemented by Core.Web.Remoting; see ApplicationHost.CreateApplicationHost.</summary>
	internal interface IApplicationHostFactory
	{
		object CreateApplicationHost (Type hostType, string virtualDir, string physicalDir);
	}
}
