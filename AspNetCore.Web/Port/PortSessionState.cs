//
// Which store answers <sessionState mode="StateServer">.
//
// Upstream hard-codes Mono's SessionStateServerHandler, a client for a RemoteStateServer published over
// .NET Remoting. This port has two stores for that mode, and neither is in this assembly:
//
//   Core.Web.SessionState  DistributedCacheSessionStateStore - the default; an IDistributedCache
//   Core.Web.Remoting      SessionStateServerHandler         - upstream's client, over tcp remoting,
//                                                              talking to a separate state server
//
// SessionStateModule reads the provider type from here by NAME (Tools/port-patches.txt), so Core.Web
// references neither assembly. The host picks one before the first request - see
// Core.Web.Remoting's UseWebFormsRemoteStateServer. web.config is the same for both.
//

namespace System.Web.SessionState
{
	internal static class PortSessionState
	{
		internal const string DistributedCacheStore =
			"System.Web.SessionState.DistributedCacheSessionStateStore, Core.Web.SessionState";

		/// <summary>
		/// Assembly-qualified provider type for mode="StateServer". Read when SessionStateModule
		/// initialises, i.e. on the first request, so it has to be set before that.
		/// </summary>
		internal static string StateServerProviderType = DistributedCacheStore;
	}
}
