//
// What HttpRuntime.UnloadAppDomain means when there is no AppDomain to unload.
//
// Upstream's own file watchers call HttpRuntime.UnloadAppDomain when bin, App_Code, Global.asax or a
// web.config changes, and so does application code. On .NET Framework that ended in
// AppDomain.Unload (AppDomain.CurrentDomain) and the host started a fresh domain for the next request.
// On .NET Core AppDomain.Unload throws, and HttpRuntime swallows it.
//
// When the application runs in a child domain (Core.Web.Remoting), the domain IS a process that its
// parent can replace, so the parent needs to hear about the request. HttpRuntime calls
// OnUnloadRequested at the moment it marks the domain as unloading (Tools/port-patches.txt), which is
// the earliest point: from then on it refuses new requests, so the parent must stop routing them here.
//
// A host with nothing to replace - a single application in its own Kestrel process - installs nothing,
// and the behaviour is exactly upstream's.
//

using System;

namespace System.Web.Hosting
{
	internal static class PortApplicationLifetime
	{
		/// <summary>
		/// Called once per unload request with the recorded shutdown reason. Installed by the hosting
		/// layer before the application starts; null when nothing can act on it.
		/// </summary>
		internal static Action<ApplicationShutdownReason> UnloadRequested;

		internal static void OnUnloadRequested ()
		{
			Action<ApplicationShutdownReason> handler = UnloadRequested;
			if (handler == null)
				return;

			try {
				handler (HostingEnvironment.ShutdownReason);
			} catch (Exception e) {
				// UnloadAppDomain is called from file-watcher threads; an exception escaping here would
				// take the process down instead of recycling it.
				Console.Error.WriteLine (e);
			}
		}
	}
}
