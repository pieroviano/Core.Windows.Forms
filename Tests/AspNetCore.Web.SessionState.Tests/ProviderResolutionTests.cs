//
// The seam between SessionStateModule and these stores.
//
// Core.Web must not reference Core.Web.SessionState - that would put Microsoft.Data.SqlClient in front
// of every application whether it uses session state or not - so the patched SessionStateModule names
// the stores as assembly-qualified STRINGS and the provider model loads them at runtime.
//
// Nothing else checks that those strings are right. A namespace change, a class rename or an
// AssemblyName change compiles perfectly on both sides and fails on the first request that touches
// Session, in whichever mode nobody happened to test. So the names are read back out of the patched
// source and resolved here.
//

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.SessionState;
using Xunit;

namespace WebFormsPort.SessionStateTests
{
	public class ProviderResolutionTests
	{
		const string PatchedModule =
			@"AspNetCore.Web\patched\System.Web.SessionState_2.0__SessionStateModule.cs";

		[Fact]
		public void Every_store_the_patched_module_names_can_actually_be_loaded ()
		{
			string source = File.ReadAllText (Path.Combine (RepoPaths.Root, PatchedModule));

			// The two ProviderSettings lines the port's patch rules rewrote, e.g.
			//   new ProviderSettings (null, "System.Web.SessionState.SqlSessionStateStore, Core.Web.SessionState")
			MatchCollection matches = Regex.Matches (
				source, @"new ProviderSettings \(null, ""(?<type>[^""]+, Core\.Web\.SessionState)""\)");

			Assert.Equal (2, matches.Count);

			foreach (Match match in matches) {
				string name = match.Groups ["type"].Value;
				Type resolved = Type.GetType (name, throwOnError: false);

				Assert.True (resolved != null,
					     "SessionStateModule names \"" + name + "\", which does not resolve. The patch " +
					     "rule in Tools/port-patches.txt and the type in Core.Web.SessionState have " +
					     "drifted apart; a session request in that mode would fail at runtime.");

				Assert.True (typeof (SessionStateStoreProviderBase).IsAssignableFrom (resolved),
					     name + " is not a SessionStateStoreProviderBase.");

				// ProvidersHelper.InstantiateProvider uses Activator.CreateInstance, so a store that
				// grew a constructor parameter would resolve here and still fail to build.
				Assert.NotNull (resolved.GetConstructor (Type.EmptyTypes));
			}
		}

		[Fact]
		public void Both_modes_are_covered_and_they_are_different_stores ()
		{
			string source = File.ReadAllText (Path.Combine (RepoPaths.Root, PatchedModule));

			Assert.Contains ("System.Web.SessionState.DistributedCacheSessionStateStore, Core.Web.SessionState",
					 source);
			Assert.Contains ("System.Web.SessionState.SqlSessionStateStore, Core.Web.SessionState", source);

			// InProc keeps Mono's own handler - it needs no substitution and should not have been
			// caught by the patch rules.
			Assert.Contains ("typeof (SessionInProcHandler)", source);
		}
	}
}
