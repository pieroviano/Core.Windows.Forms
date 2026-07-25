//
// The seam between dependency injection and the provider model.
//
// SessionStateModule builds its store through ProvidersHelper.InstantiateProvider, which is
// Activator.CreateInstance with a parameterless constructor. There is no DI there and there cannot
// be: the module is constructed by HttpApplication long before anything hands it a service provider,
// and the provider model's contract is a type name in configuration.
//
// So the host deposits what the stores need here at startup, and they collect it when the module
// instantiates them. This is the same shape as System.Web.Util.StateSerializerAccessor, for the same
// reason.
//
// Set these BEFORE the first request. UseWebForms initialises the runtime, and the first request to
// touch Session constructs the store; anything assigned afterwards is simply too late, which is why
// Configure throws rather than quietly taking effect on the next process start.
//

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Distributed;

namespace System.Web.SessionState
{
	/// <summary>
	/// Services the out-of-process session stores need, supplied by the host before the first request.
	/// </summary>
	public static class SessionStateHostServices
	{
		static IDistributedCache distributedCache;
		static string sqlConnectionString;
		static bool frozen;

		/// <summary>
		/// Backing store for <c>mode="StateServer"</c>. Any <see cref="IDistributedCache"/> will do -
		/// Redis, SQL Server, NCache, or the in-memory one for a single instance.
		/// </summary>
		public static IDistributedCache DistributedCache {
			get { return distributedCache; }
			set {
				ThrowIfFrozen (nameof (DistributedCache));
				distributedCache = value;
			}
		}

		/// <summary>
		/// Connection string for <c>mode="SQLServer"</c>, overriding
		/// <c>&lt;sessionState sqlConnectionString&gt;</c>. Useful when the real credentials come from a
		/// secret store rather than from <c>web.config</c>.
		/// </summary>
		public static string SqlConnectionString {
			get { return sqlConnectionString; }
			set {
				ThrowIfFrozen (nameof (SqlConnectionString));
				sqlConnectionString = value;
			}
		}

		/// <summary>
		/// Called by a store as it initialises. After this, assignment throws rather than silently
		/// having no effect - a configuration change that appears to be accepted and is not is far
		/// worse to diagnose than one that fails immediately.
		/// </summary>
		internal static void Freeze ()
		{
			frozen = true;
		}

		/// <summary>Test seam: undo <see cref="Freeze"/>. Not for application use.</summary>
		internal static void Reset ()
		{
			frozen = false;
			distributedCache = null;
			sqlConnectionString = null;
		}

		static void ThrowIfFrozen (string member)
		{
			if (frozen)
				throw new InvalidOperationException (
					"SessionStateHostServices." + member + " was set after the session store had already " +
					"been created. The store is built once, by SessionStateModule, on the first request " +
					"that touches Session - set this during host startup, before app.Run ().");
		}
	}
}
