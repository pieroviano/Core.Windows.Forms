//
// ApplicationHost.CreateApplicationHost, on a child domain.
//
// Core.Web's ApplicationHost finds this by name. The contract is upstream's: a new domain configured for
// the application, the host type created inside it, and the caller left holding a reference to it.
// Differences that follow from the domain being a process:
//
//   * the host type is used through a generated proxy, so its members must be virtual (or the host
//     exposed through an interface) - the proxy factory names any that are not;
//   * arguments and results must be serializable or MarshalByRefObject, as they always had to be;
//   * HttpRuntime.UnloadAppDomain inside the application ends the process, and calls on the host
//     then fail with AppDomainUnloadedException, as they did after an AppDomain unload.
//

using System;
using System.Runtime.Remoting;
using System.Threading;

namespace System.Web.Hosting
{
	internal sealed class RemotingApplicationHostFactory : IApplicationHostFactory
	{
		public object CreateApplicationHost (Type hostType, string virtualDir, string physicalDir)
		{
			if (String.IsNullOrEmpty (physicalDir))
				throw new ArgumentNullException (nameof (physicalDir));

			ApplicationDomainSettings settings = ApplicationDomainSettings.FromCurrentRuntime (virtualDir, physicalDir);
			var events = new UnloadOnRequest ();
			ChildAppDomain domain = ApplicationDomains.Start (settings, events, TimeSpan.FromMinutes (2));
			events.Domain = domain;

			RemotingConfiguration.AllowAssembly (hostType.Assembly);
			return domain.CreateInstanceAndUnwrap (hostType.Assembly.FullName, hostType.FullName);
		}

		/// <summary>An application that asks to be unloaded is: nothing else owns this domain.</summary>
		sealed class UnloadOnRequest : MarshalByRefObject, IApplicationDomainEvents
		{
			public ChildAppDomain Domain;

			public void UnloadRequested (string reason)
			{
				ChildAppDomain domain = Domain;
				if (domain != null)
					ThreadPool.QueueUserWorkItem (_ => domain.Unload ());
			}
		}
	}
}
