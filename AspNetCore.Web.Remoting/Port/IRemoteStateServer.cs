//
// The remote contract for Mono's RemoteStateServer.
//
// Upstream publishes RemoteStateServer itself - an internal class - and the client talks to it through
// a transparent proxy of that class. Net4x.Runtime.Remoting generates proxies with Reflection.Emit, and
// a dynamic assembly can neither subclass nor implement a non-public type, so the contract has to be a
// public interface. RemoteStateServer implements it through patches (Tools/port-patches.txt) and stays
// internal.
//
// GetItem answers object rather than StateServerItem: the item type is internal to Core.Web and cannot
// appear in a public signature. It crosses the wire as the StateServerItem it is, and the client casts.
//

using System;
using System.ComponentModel;

namespace System.Web.SessionState
{
	/// <summary>Port plumbing for <c>mode="StateServer"</c> over remoting. Not for application use.</summary>
	[EditorBrowsable (EditorBrowsableState.Never)]
	public interface IRemoteStateServer
	{
		void CreateUninitializedItem (string id, int timeout);

		object GetItem (string id, out bool locked, out TimeSpan lockAge, out object lockId,
				out SessionStateActions actions, bool exclusive);

		void Remove (string id, object lockid);

		void ResetItemTimeout (string id);

		void ReleaseItemExclusive (string id, object lockId);

		void SetAndReleaseItemExclusive (string id, byte [] collection_data, byte [] sobjs_data,
						 object lockId, int timeout, bool newItem);
	}
}
