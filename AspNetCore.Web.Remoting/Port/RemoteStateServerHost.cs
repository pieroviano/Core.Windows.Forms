//
// The state server: what aspnet_state.exe was, for <sessionState mode="StateServer"> clients built on
// Mono's SessionStateServerHandler.
//
// Mono's client connects to "tcp://<server>:<port>/StateServer" from stateConnectionString
// ("tcpip=127.0.0.1:42424") and expects a RemoteStateServer published there. This publishes one.
//
// Two defaults are taken from aspnet_state rather than from the remoting library:
//
//   port 42424         the documented StateServer port, and what stateConnectionString examples use;
//   loopback only      aspnet_state refused remote connections unless AllowRemoteConnection was set in
//                      the registry. A session store holds authentication state and whatever else an
//                      application put in Session, and it has no authentication of its own.
//
// Not wire-compatible with aspnet_state.exe: both ends must be this port.
//

using System;
using System.Net;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;

namespace System.Web.SessionState
{
	public sealed class RemoteStateServerHost : IDisposable
	{
		/// <summary>The StateServer port aspnet_state.exe listened on.</summary>
		public const int DefaultPort = 42424;

		/// <summary>The object uri Mono's SessionStateServerHandler connects to.</summary>
		internal const string ObjectUri = "StateServer";

		static readonly object sync = new object ();
		static bool published;

		readonly TcpServerChannel channel;

		RemoteStateServerHost (TcpServerChannel channel)
		{
			this.channel = channel;
		}

		/// <summary>The port actually bound - differs from the requested one when 0 was requested.</summary>
		public int Port {
			get { return channel.Port; }
		}

		/// <summary>
		/// Publishes the state server on <paramref name="port"/>. Sessions live in this process, so it
		/// must outlive every application that uses it.
		/// </summary>
		/// <param name="allowRemoteConnection">
		/// Accept connections from other machines. Off by default, as aspnet_state was: the store is
		/// unauthenticated.
		/// </param>
		public static RemoteStateServerHost Start (int port = DefaultPort, bool allowRemoteConnection = false)
		{
			AllowStateServerTypes ();

			var tcp = new TcpServerChannel ("aspnet_state:" + port, port) {
				BindAddress = allowRemoteConnection ? IPAddress.Any : IPAddress.Loopback,
			};
			ChannelServices.RegisterChannel (tcp, false);

			lock (sync) {
				// A well-known registration is process-wide and cannot be withdrawn, so a second host in
				// the same process adds a listener for the one server rather than failing.
				if (!published) {
					RemotingConfiguration.RegisterWellKnownServiceType (
						typeof (RemoteStateServer), ObjectUri, WellKnownObjectMode.Singleton);
					published = true;
				}
			}

			return new RemoteStateServerHost (tcp);
		}

		/// <summary>Stops listening. Sessions already stored stay in memory until the process ends.</summary>
		public void Dispose ()
		{
			try {
				ChannelServices.UnregisterChannel (channel);
			} catch (RemotingException) {
				// Already unregistered.
			}
		}

		/// <summary>
		/// Lets both ends deserialize what a state server call carries. The item and the lock-state enum
		/// live in Core.Web, which registering the service does not allow-list - only the service's own
		/// assembly is.
		/// </summary>
		internal static void AllowStateServerTypes ()
		{
			RemotingConfiguration.AllowAssembly (typeof (StateServerItem).Assembly);
			RemotingConfiguration.AllowAssembly (typeof (IRemoteStateServer).Assembly);
		}

		/// <summary>
		/// Makes <c>&lt;sessionState mode="StateServer"&gt;</c> use a remote state server - see
		/// <see cref="Start"/> - instead of the default IDistributedCache store. Call before the first
		/// request; web.config is unchanged, and <c>stateConnectionString</c> names the server.
		/// </summary>
		public static void UseForStateServerMode ()
		{
			PortSessionState.StateServerProviderType =
				"System.Web.SessionState.SessionStateServerHandler, Core.Web.Remoting";
		}
	}
}
