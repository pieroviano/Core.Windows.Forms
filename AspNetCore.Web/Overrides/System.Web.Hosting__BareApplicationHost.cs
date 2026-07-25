//
// System.Web.Hosting.BareApplicationHost - port override
//
// Upstream: mono/mcs/class/System.Web/System.Web.Hosting/BareApplicationHost.cs
//
// The class is really just the IRegisteredObject registry that HostingEnvironment delegates to.
// Only the surrounding AppDomain plumbing had to go:
//
//   * MarshalByRefObject base       - no cross-AppDomain marshalling on .NET Core;
//   * AppDomain .appPath/.appVPath  - paths are now supplied by WebFormsRuntimeHost;
//   * DomainUnload hook             - replaced by StopAll(), which the Kestrel host calls from
//                                     IHostApplicationLifetime.ApplicationStopping;
//   * ApplicationManager Manager    - there is one application per process, so nothing to notify.
//
// Keeping this type (rather than deleting it as originally planned) means HostingEnvironment
// compiles unmodified and QueueBackgroundWorkItem/RegisterObject keep working.
//

using System;
using System.Collections.Generic;

namespace System.Web.Hosting
{
	class RegisteredItem
	{
		public IRegisteredObject Item;
		public bool AutoClean;

		public RegisteredItem (IRegisteredObject item, bool autoclean)
		{
			this.Item = item;
			this.AutoClean = autoclean;
		}
	}

	sealed class BareApplicationHost
	{
		string vpath;
		string phys_path;
		Dictionary<Type, RegisteredItem> hash;
		string codegen_dir;

		public BareApplicationHost ()
			: this (null, null, null)
		{
		}

		public BareApplicationHost (string virtualPath, string physicalPath, string codeGenDir)
		{
			hash = new Dictionary<Type, RegisteredItem> ();
			vpath = virtualPath;
			phys_path = physicalPath;
			codegen_dir = codeGenDir;
			HostingEnvironment.Host = this;
		}

		public string VirtualPath {
			get { return vpath; }
		}

		public string PhysicalPath {
			get { return phys_path; }
		}

		public void Shutdown ()
		{
			HostingEnvironment.InitiateShutdown ();
		}

		public void StopObject (Type type)
		{
			RegisteredItem reg;
			lock (hash) {
				if (!hash.TryGetValue (type, out reg))
					return;
			}
			reg.Item.Stop (false);
		}

		public IRegisteredObject CreateInstance (Type type)
		{
			return (IRegisteredObject) Activator.CreateInstance (type, null);
		}

		public void RegisterObject (IRegisteredObject obj, bool auto_clean)
		{
			lock (hash)
				hash [obj.GetType ()] = new RegisteredItem (obj, auto_clean);
		}

		public bool UnregisterObject (IRegisteredObject obj)
		{
			lock (hash)
				return hash.Remove (obj.GetType ());
		}

		public IRegisteredObject GetObject (Type type)
		{
			RegisteredItem reg;
			lock (hash) {
				if (hash.TryGetValue (type, out reg))
					return reg.Item;
			}
			return null;
		}

		public string GetCodeGenDir ()
		{
			return codegen_dir;
		}

		// Replaces upstream's AppDomain.DomainUnload handler. Called by the host on shutdown.
		internal void StopAll ()
		{
			RegisteredItem [] objects;
			lock (hash) {
				objects = new RegisteredItem [hash.Count];
				hash.Values.CopyTo (objects, 0);
				hash.Clear ();
			}

			foreach (RegisteredItem reg in objects) {
				try {
					reg.Item.Stop (true); // Stop should call Unregister. It's ok if not.
				} catch {
					// a failing shutdown callback must not prevent the others from running
				}
			}
		}
	}
}
