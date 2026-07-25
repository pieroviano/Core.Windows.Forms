//
// Points the shipping System.Configuration.ConfigurationManager at the ported configuration system.
//
// Third-party libraries are compiled against that package's strong-named identity, and nothing this
// port can build will satisfy such a reference (a [TypeForwardedTo] facade fails the public-key
// check; an AssemblyLoadContext.Resolving hook is rejected because the resolved assembly's simple
// name must match). So the package stays, and instead its ConfigurationManager is told where to get
// its sections from - the same mechanism the port uses for Mono's ConfigurationManager, and the same
// one .NET Framework's own System.Web used.
//
// This assembly is the only one in the port compiled against the package's configuration types. That
// isolation is what makes it work: everything crossing back to the rest of the port is a primitive or
// a shared framework type (string, NameValueCollection), so the package's System.Configuration.* and
// Core.Configuration's never appear in the same signature.
//

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Internal;
using System.Reflection;

namespace System.Web.Configuration.Bridge
{
	/// <summary>
	/// One connection string as plain data, so the caller need not reference either
	/// System.Configuration implementation to describe one.
	/// </summary>
	public sealed class BridgedConnectionString
	{
		public BridgedConnectionString (string name, string connectionString, string providerName)
		{
			Name = name;
			ConnectionString = connectionString;
			ProviderName = providerName;
		}

		public string Name { get; }
		public string ConnectionString { get; }
		public string ProviderName { get; }
	}

	public static class PackagedConfigurationBridge
	{
		static bool installed;
		static readonly object sync = new object ();

		/// <summary>
		/// Redirects the packaged ConfigurationManager at the ported configuration system.
		/// </summary>
		/// <param name="appSettings">
		/// Supplies &lt;appSettings&gt;. NameValueCollection is a shared framework type, so it can be
		/// handed straight back to callers of ConfigurationManager.AppSettings.
		/// </param>
		/// <param name="connectionStrings">
		/// Supplies &lt;connectionStrings&gt; as plain data; this method builds the package's own
		/// ConnectionStringSettingsCollection from it, which is what its ConnectionStrings property casts to.
		/// </param>
		/// <param name="otherSection">
		/// Optional fallback for any other section name. Returning null is fine and usually right - a
		/// section object from the ported implementation would be the wrong CLR type for a caller
		/// compiled against the package, and returning it would raise InvalidCastException inside
		/// somebody else's code. Null simply reads as "not configured".
		/// </param>
		/// <returns>true if the redirect was installed, false if the runtime did not expose the hook.</returns>
		public static bool Install (Func<NameValueCollection> appSettings,
					    Func<IEnumerable<BridgedConnectionString>> connectionStrings,
					    Func<string, object>? otherSection = null)
		{
			if (appSettings == null)
				throw new ArgumentNullException (nameof (appSettings));
			if (connectionStrings == null)
				throw new ArgumentNullException (nameof (connectionStrings));

			lock (sync) {
				if (installed)
					return true;

				var system = new BridgedConfigSystem (appSettings, connectionStrings, otherSection);

				// Same internal entry point .NET Framework's System.Web used. Reflection because it is
				// not public; absence is reported rather than thrown, since the only consequence is
				// that third-party configuration reads keep returning nothing.
				MethodInfo? set = typeof (ConfigurationManager).GetMethod (
					"SetConfigurationSystem",
					BindingFlags.Static | BindingFlags.NonPublic,
					null,
					new [] { typeof (IInternalConfigSystem), typeof (bool) },
					null);
				if (set == null)
					return false;

				try {
					set.Invoke (null, new object [] { system, false });
				} catch (TargetInvocationException) {
					// Already initialised by something that read configuration before us.
					return false;
				}

				MethodInfo? complete = typeof (ConfigurationManager).GetMethod (
					"CompleteConfigInit", BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
				if (complete != null) {
					try {
						complete.Invoke (null, null);
					} catch (TargetInvocationException) {
						// non-fatal: GetSection already routes through the installed system
					}
				}

				installed = true;
				return true;
			}
		}

		sealed class BridgedConfigSystem : IInternalConfigSystem
		{
			readonly Func<NameValueCollection> appSettings;
			readonly Func<IEnumerable<BridgedConnectionString>> connectionStrings;
			readonly Func<string, object>? otherSection;

			internal BridgedConfigSystem (Func<NameValueCollection> appSettings,
						      Func<IEnumerable<BridgedConnectionString>> connectionStrings,
						      Func<string, object>? otherSection)
			{
				this.appSettings = appSettings;
				this.connectionStrings = connectionStrings;
				this.otherSection = otherSection;
			}

			public bool SupportsUserConfig {
				get { return false; }
			}

			public object? GetSection (string configKey)
			{
				switch (configKey) {
				case "appSettings":
					return appSettings ();
				case "connectionStrings":
					return BuildConnectionStringsSection ();
				}

				return otherSection == null ? null : otherSection (configKey);
			}

			public void RefreshConfig (string sectionName)
			{
			}

			// ConfigurationManager.ConnectionStrings casts this section to the package's
			// ConnectionStringsSection, so it has to be that exact type - built here, where the
			// package is the only System.Configuration on the compile path.
			ConnectionStringsSection BuildConnectionStringsSection ()
			{
				var section = new ConnectionStringsSection ();
				IEnumerable<BridgedConnectionString> items = connectionStrings ();
				if (items == null)
					return section;

				foreach (BridgedConnectionString item in items) {
					if (item == null || String.IsNullOrEmpty (item.Name))
						continue;
					section.ConnectionStrings.Add (
						new ConnectionStringSettings (item.Name, item.ConnectionString, item.ProviderName));
				}

				return section;
			}
		}
	}
}
