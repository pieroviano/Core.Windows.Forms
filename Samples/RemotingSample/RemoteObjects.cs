//
// The types this application publishes over remoting, and the host objects it offers to
// ApplicationHost.CreateApplicationHost and ApplicationManager.
//
// Class contracts keep every member virtual: a remoting proxy is a generated subclass, and a member it
// cannot override would run on the caller instead of here. Interface contracts need nothing.
//

using System;
using System.IO;
using System.Web;
using System.Web.Hosting;

namespace RemotingSample
{
	public interface ICalculator
	{
		int Add (int a, int b);

		DivisionResult Divide (int dividend, int divisor);

		/// <summary>Where the call ran: "&lt;process id&gt; &lt;application virtual path&gt;".</summary>
		string WhereAmI ();
	}

	[Serializable]
	public class DivisionResult
	{
		public int Quotient;
		public int Remainder;
	}

	/// <summary>Published as a Singleton at Calculator.rem by web.config.</summary>
	public class Calculator : MarshalByRefObject, ICalculator
	{
		public int Add (int a, int b) => a + b;

		public DivisionResult Divide (int dividend, int divisor)
			=> new DivisionResult { Quotient = dividend / divisor, Remainder = dividend % divisor };

		public string WhereAmI () => Environment.ProcessId + " " + HttpRuntime.AppDomainAppVirtualPath;
	}

	/// <summary>Client-activated: every activation is its own instance with its own state.</summary>
	public class Counter : MarshalByRefObject
	{
		int value;

		// A class proxy derives from the contract, so the contract needs a constructor it can call.
		protected Counter ()
		{
		}

		public Counter (int start)
		{
			value = start;
		}

		public virtual int Next () => ++value;
	}

	/// <summary>
	/// A host object for ApplicationHost.CreateApplicationHost: created inside the application's own
	/// domain, where HttpRuntime is initialised for it.
	/// </summary>
	public class ApplicationProbe : MarshalByRefObject
	{
		public virtual int ProcessId => Environment.ProcessId;

		public virtual string ApplicationVirtualPath => HttpRuntime.AppDomainAppVirtualPath;

		public virtual string ApplicationPhysicalPath => HttpRuntime.AppDomainAppPath;

		/// <summary>Runs a page through the full pipeline with upstream's SimpleWorkerRequest.</summary>
		public virtual string Render (string page, string query)
		{
			using (var output = new StringWriter ()) {
				HttpRuntime.ProcessRequest (new SimpleWorkerRequest (page, query, output));
				return output.ToString ();
			}
		}
	}

	/// <summary>A registered object for ApplicationManager.CreateObject.</summary>
	public class RegisteredProbe : ApplicationProbe, IRegisteredObject
	{
		public virtual bool Stopped { get; private set; }

		public virtual void Stop (bool immediate)
		{
			Stopped = true;
			HostingEnvironment.UnregisterObject (this);
		}
	}
}
