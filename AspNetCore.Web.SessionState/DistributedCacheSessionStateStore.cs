//
// <sessionState mode="StateServer">, backed by IDistributedCache.
//
// Why not the real State Server
// -----------------------------
// Mono implements StateServer over .NET Remoting - Activator.GetObject against a MarshalByRefObject.
// Remoting's transparent proxies are a CLR feature CoreCLR does not have, so that code cannot run
// here at all, and it was never wire-compatible with Microsoft's aspnet_state.exe either.
//
// Speaking aspnet_state.exe's own protocol was the other candidate. It was rejected because the win
// is smaller than it looks: the payload inside is BinaryFormatter, which .NET 9+ cannot read, so a
// ported application could reuse the State Server MACHINE but never actually share a session with a
// .NET Framework application. That leaves the transport buying nothing but a Windows-only dependency
// on a service Microsoft no longer develops.
//
// IDistributedCache buys the thing the limitation was actually about - running more than one instance.
// Redis, SQL Server, NCache, or the in-memory implementation for a single box; the application still
// says mode="StateServer" and never learns the difference.
//
// Locking, honestly
// -----------------
// SessionStateStoreProviderBase is written around exclusive locks, and IDistributedCache has no
// compare-and-swap - only Get and Set. Acquisition here is therefore read-modify-write, and two
// requests for the SAME session arriving at two instances in the same instant can both believe they
// took the lock.
//
// That is a narrower window than it sounds (it needs concurrent requests for one session id, which in
// practice means frames, parallel AJAX or a double-submit) and the consequence is last-writer-wins on
// the session, not corruption. It is still a real difference from SQL Server mode, where the lock is a
// row update and genuinely atomic. If overlapping writes to a single session must not be lost, use
// mode="SQLServer".
//

using System;
using System.Collections.Specialized;
using System.IO;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using Microsoft.Extensions.Caching.Distributed;

namespace System.Web.SessionState
{
	/// <summary>
	/// Session store for <c>mode="StateServer"</c>, holding sessions in an
	/// <see cref="IDistributedCache"/> supplied through <see cref="SessionStateHostServices"/>.
	/// </summary>
	public class DistributedCacheSessionStateStore : SessionStateStoreProviderBase
	{
		const byte FormatVersion = 1;

		IDistributedCache cache;
		SessionStateSection config;
		string keyPrefix;
		int timeoutMinutes;
		bool compress;

		public override void Initialize (string name, NameValueCollection providerConfig)
		{
			if (String.IsNullOrEmpty (name))
				name = "DistributedCacheSessionStateStore";

			base.Initialize (name, providerConfig ?? new NameValueCollection ());

			cache = SessionStateHostServices.DistributedCache;
			if (cache == null)
				throw new HttpException (
					"<sessionState mode=\"StateServer\"> is configured, but no IDistributedCache was " +
					"supplied. This port maps StateServer onto IDistributedCache rather than " +
					"aspnet_state.exe; register one at startup - " +
					"builder.Services.AddStackExchangeRedisCache (...) or AddDistributedMemoryCache () " +
					"for a single instance - and call builder.Services.AddWebFormsSessionState ().");

			SessionStateHostServices.Freeze ();

			config = WebConfigurationManager.GetWebApplicationSection ("system.web/sessionState")
				as SessionStateSection;

			timeoutMinutes = config != null ? (int) config.Timeout.TotalMinutes : 20;
			if (timeoutMinutes <= 0)
				timeoutMinutes = 20;

			compress = config != null && config.CompressionEnabled;

			keyPrefix = GetKeyPrefix (HostingEnvironment.ApplicationVirtualPath);
		}

		/// <summary>
		/// The cache key a session is stored under. Public because an operator looking at Redis needs
		/// to know what to look for, and because it is the only way to correlate a session id in a log
		/// with an entry in the store.
		/// </summary>
		/// <remarks>
		/// Namespaced by application: session ids are only unique within an application, so two
		/// applications sharing one cache would otherwise read each other's sessions.
		/// </remarks>
		public static string GetCacheKey (string applicationVirtualPath, string sessionId)
		{
			return GetKeyPrefix (applicationVirtualPath) + sessionId;
		}

		static string GetKeyPrefix (string applicationVirtualPath)
		{
			return "aspnet:session:" +
				(applicationVirtualPath ?? "/").Trim ('/').ToLowerInvariant () + ":";
		}

		public override void Dispose ()
		{
		}

		public override void InitializeRequest (HttpContext context)
		{
		}

		public override void EndRequest (HttpContext context)
		{
		}

		/// <summary>
		/// Always false. Session_End needs the store to notice an expiry nobody asked about, and a
		/// distributed cache simply drops the key - there is no callback to hook. Returning false is
		/// the contract's way of saying so, and the module then skips Session_End rather than
		/// pretending it fired.
		/// </summary>
		public override bool SetItemExpireCallback (SessionStateItemExpireCallback expireCallback)
		{
			return false;
		}

		public override SessionStateStoreData CreateNewStoreData (HttpContext context, int timeout)
		{
			return SessionStateSerializer.CreateStoreData (context, new SessionStateItemCollection (), timeout);
		}

		public override void CreateUninitializedItem (HttpContext context, string id, int timeout)
		{
			// Written by the module for a cookieless session before the request that will fill it.
			// The InitializeItem flag is what tells the next reader "this is a placeholder, run
			// Session_Start", so it has to survive the round trip.
			var entry = new Entry {
				Timeout = timeout,
				Flags = (int) SessionStateActions.InitializeItem,
				Payload = SessionStateSerializer.SerializeItems (new SessionStateItemCollection (), compress),
			};

			Write (id, entry);
		}

		public override SessionStateStoreData GetItem (HttpContext context, string id, out bool locked,
							       out TimeSpan lockAge, out object lockId,
							       out SessionStateActions actions)
		{
			return Get (context, id, exclusive: false, locked: out locked, lockAge: out lockAge,
				    lockId: out lockId, actions: out actions);
		}

		public override SessionStateStoreData GetItemExclusive (HttpContext context, string id, out bool locked,
								       out TimeSpan lockAge, out object lockId,
								       out SessionStateActions actions)
		{
			return Get (context, id, exclusive: true, locked: out locked, lockAge: out lockAge,
				    lockId: out lockId, actions: out actions);
		}

		SessionStateStoreData Get (HttpContext context, string id, bool exclusive, out bool locked,
					   out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			locked = false;
			lockAge = TimeSpan.Zero;
			lockId = null;
			actions = SessionStateActions.None;

			Entry entry = Read (id);
			if (entry == null)
				return null;

			if (entry.Locked) {
				// Report the lock rather than the data. The module retries, and gives up with
				// "Timeout expired" once it has waited executionTimeout - the same behaviour as SQL
				// Server mode.
				locked = true;
				lockAge = DateTime.UtcNow - entry.LockDateUtc;
				lockId = entry.LockId;
				return null;
			}

			actions = (SessionStateActions) entry.Flags;

			if (exclusive) {
				entry.Locked = true;
				entry.LockId = entry.LockId + 1;
				entry.LockDateUtc = DateTime.UtcNow;
				// The flag is consumed here, not on release: the caller is about to initialise the
				// item, and a retry after a crash should not run Session_Start twice.
				entry.Flags = (int) SessionStateActions.None;
				Write (id, entry);
				lockId = entry.LockId;
			}

			SessionStateItemCollection items = actions == SessionStateActions.InitializeItem
				? new SessionStateItemCollection ()
				: SessionStateSerializer.DeserializeItems (entry.Payload, compress);

			return SessionStateSerializer.CreateStoreData (context, items, entry.Timeout);
		}

		public override void ReleaseItemExclusive (HttpContext context, string id, object lockId)
		{
			Entry entry = Read (id);
			if (entry == null || !Owns (entry, lockId))
				return;

			entry.Locked = false;
			Write (id, entry);
		}

		public override void SetAndReleaseItemExclusive (HttpContext context, string id,
								 SessionStateStoreData item, object lockId, bool newItem)
		{
			byte [] payload;
			try {
				payload = SessionStateSerializer.SerializeItems (item.Items, compress);
			} catch (Exception ex) {
				throw SessionStateSerializer.DescribeSerializationFailure (ex, "StateServer");
			}

			if (!newItem) {
				Entry existing = Read (id);
				// A lock that has expired or been taken by someone else means this request no longer
				// owns the session; writing anyway would clobber whoever does.
				if (existing != null && !Owns (existing, lockId))
					return;
			}

			Write (id, new Entry {
				Timeout = item.Timeout > 0 ? item.Timeout : timeoutMinutes,
				Flags = (int) SessionStateActions.None,
				Locked = false,
				Payload = payload,
			});
		}

		public override void RemoveItem (HttpContext context, string id, object lockId, SessionStateStoreData item)
		{
			Entry entry = Read (id);
			if (entry != null && !Owns (entry, lockId))
				return;

			cache.Remove (Key (id));
		}

		public override void ResetItemTimeout (HttpContext context, string id)
		{
			// Re-writing the entry is what slides the expiration. IDistributedCache.Refresh does the
			// same for stores that support it, and is a no-op on those that do not - so the write is
			// the reliable one.
			Entry entry = Read (id);
			if (entry != null)
				Write (id, entry);
		}

		static bool Owns (Entry entry, object lockId)
		{
			return lockId == null || !entry.Locked || entry.LockId == Convert.ToInt32 (lockId);
		}

		string Key (string id)
		{
			return keyPrefix + id;
		}

		Entry Read (string id)
		{
			byte [] raw = cache.Get (Key (id));
			return raw == null ? null : Entry.Decode (raw);
		}

		void Write (string id, Entry entry)
		{
			int minutes = entry.Timeout > 0 ? entry.Timeout : timeoutMinutes;

			cache.Set (Key (id), entry.Encode (), new DistributedCacheEntryOptions {
				// Sliding, not absolute: session timeout has always meant "idle for this long", and
				// every request that touches Session rewrites the entry.
				SlidingExpiration = TimeSpan.FromMinutes (minutes),
			});
		}

		/// <summary>
		/// One session in the cache: the payload plus the lock, in a single value.
		/// </summary>
		/// <remarks>
		/// Deliberately one key rather than a data key and a lock key. IDistributedCache can only
		/// write one key atomically, so splitting them would allow a lock to outlive the data it
		/// guards - a session that can never be read again.
		/// </remarks>
		sealed class Entry
		{
			public int Timeout;
			public int Flags;
			public bool Locked;
			public int LockId;
			public DateTime LockDateUtc = DateTime.UtcNow;
			public byte [] Payload;

			public byte [] Encode ()
			{
				using (var buffer = new MemoryStream ())
				using (var writer = new BinaryWriter (buffer)) {
					writer.Write (FormatVersion);
					writer.Write (Timeout);
					writer.Write (Flags);
					writer.Write (Locked);
					writer.Write (LockId);
					writer.Write (LockDateUtc.Ticks);
					writer.Write (Payload != null ? Payload.Length : 0);
					if (Payload != null)
						writer.Write (Payload);

					writer.Flush ();
					return buffer.ToArray ();
				}
			}

			public static Entry Decode (byte [] raw)
			{
				using (var buffer = new MemoryStream (raw))
				using (var reader = new BinaryReader (buffer)) {
					byte version = reader.ReadByte ();
					if (version != FormatVersion)
						// A rolling upgrade can leave both formats in one cache. Treating an
						// unreadable entry as absent costs that user their session and nothing more;
						// throwing would take out every request that touched it.
						return null;

					var entry = new Entry {
						Timeout = reader.ReadInt32 (),
						Flags = reader.ReadInt32 (),
						Locked = reader.ReadBoolean (),
						LockId = reader.ReadInt32 (),
						LockDateUtc = new DateTime (reader.ReadInt64 (), DateTimeKind.Utc),
					};

					entry.Payload = reader.ReadBytes (reader.ReadInt32 ());
					return entry;
				}
			}
		}
	}
}
