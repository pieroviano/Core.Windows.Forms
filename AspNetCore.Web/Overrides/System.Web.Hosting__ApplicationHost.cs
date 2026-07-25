//
// System.Web.Hosting.ApplicationHost - port override
//
// Upstream: mono/mcs/class/System.Web/System.Web.Hosting/ApplicationHost.cs
//
// CreateApplicationHost() built a second AppDomain and marshalled the host into it. There is
// one AppDomain on .NET Core, and this port runs exactly one application per process
// (see PORT-System.Web-Kestrel-PLAN.md, P1), so that entry point is gone. What remains are the
// two members the rest of the tree actually consumes:
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

		// Kept so that application code calling it still compiles. Creating a second hosting
		// AppDomain is not possible on .NET Core; the Kestrel host owns the single application.
		public static object CreateApplicationHost (Type hostType, string virtualDir, string physicalDir)
		{
			throw new PlatformNotSupportedException (
				"ApplicationHost.CreateApplicationHost is not supported: this port hosts a single " +
				"application per process. Initialise the runtime through the Kestrel host instead.");
		}
	}
}
