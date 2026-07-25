//
// Turning a session into bytes, and back.
//
// Both stores need exactly this and it is the one part with a real compatibility caveat, so it lives
// in one place.
//
// The item collection is written by SessionStateItemCollection.Serialize, which is upstream's own
// format, and every value in it goes through System.Web.Util.AltSerialization - which in this port is
// backed by IStateObjectSerializer rather than BinaryFormatter. That has a consequence worth being
// blunt about:
//
//   Sessions round-trip between instances of THIS port. They do not interoperate with .NET Framework.
//
// A blob written by ASP.NET on .NET Framework has BinaryFormatter payloads inside it, and there is no
// BinaryFormatter on .NET 9+ to read them. Sharing a State Server box or an ASPState database with a
// Framework application therefore does not share sessions - it shares infrastructure. Give the ported
// application its own key prefix or its own ASPState database and treat the cutover as a session
// flush, the same as any deployment that changes machine keys.
//
// Static objects (<object runat="server" scope="Session"> in Global.asax) are deliberately NOT stored.
// They are declared by the application, identical in every instance, and reconstructed from the
// context on read - which is what Mono's SQL handler does too. Storing them would put application
// metadata in every session row for nothing.
//

using System;
using System.IO;
using System.IO.Compression;
using System.Web;

namespace System.Web.SessionState
{
	static class SessionStateSerializer
	{
		/// <summary>Serialises just the item collection - the payload both stores hold.</summary>
		public static byte [] SerializeItems (ISessionStateItemCollection items, bool compress)
		{
			using (var buffer = new MemoryStream ()) {
				// Written even when there is nothing to write: an empty collection and a null one are
				// the same session as far as the application is concerned, and a zero-length blob would
				// make the two indistinguishable from a store failure.
				var collection = items as SessionStateItemCollection ?? new SessionStateItemCollection ();

				if (compress) {
					using (var gzip = new GZipStream (buffer, CompressionMode.Compress, leaveOpen: true))
					using (var writer = new BinaryWriter (gzip))
						collection.Serialize (writer);
				} else {
					using (var writer = new BinaryWriter (buffer, System.Text.Encoding.UTF8, leaveOpen: true))
						collection.Serialize (writer);
				}

				return buffer.ToArray ();
			}
		}

		public static SessionStateItemCollection DeserializeItems (byte [] payload, bool compress)
		{
			if (payload == null || payload.Length == 0)
				return new SessionStateItemCollection ();

			using (var buffer = new MemoryStream (payload)) {
				if (compress) {
					using (var gzip = new GZipStream (buffer, CompressionMode.Decompress, leaveOpen: true))
					using (var reader = new BinaryReader (gzip))
						return SessionStateItemCollection.Deserialize (reader);
				}

				using (var reader = new BinaryReader (buffer, System.Text.Encoding.UTF8, leaveOpen: true))
					return SessionStateItemCollection.Deserialize (reader);
			}
		}

		/// <summary>
		/// Wraps items in the store data the module expects, pulling static objects off the context
		/// rather than out of the store.
		/// </summary>
		public static SessionStateStoreData CreateStoreData (HttpContext context,
								     ISessionStateItemCollection items,
								     int timeout)
		{
			return new SessionStateStoreData (items ?? new SessionStateItemCollection (),
							  SessionStateUtility.GetSessionStaticObjects (context),
							  timeout);
		}

		/// <summary>
		/// Rethrows a serializer refusal with the session's own context attached.
		/// </summary>
		/// <remarks>
		/// StateSerializer refuses types it cannot faithfully encode, naming the type. That message is
		/// right but incomplete here: in-proc session state never serialises at all, so the application
		/// worked until the day someone changed one word in web.config. Saying so turns a puzzling
		/// regression into an obvious one.
		/// </remarks>
		public static Exception DescribeSerializationFailure (Exception inner, string mode)
		{
			return new HttpException (
				"A value in Session could not be serialized for out-of-process storage. " +
				"<sessionState mode=\"" + mode + "\"> has to write every session value to a store, which " +
				"mode=\"InProc\" never did - so a type that has always worked can start failing the moment " +
				"the mode changes, with no other edit. Either keep only serializable values in Session, " +
				"or supply a WebFormsOptions.StateSerializer that handles this type. See the inner " +
				"exception for which type it was.", inner);
		}
	}
}
