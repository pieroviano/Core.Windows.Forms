//
// Replaces Thread.Abort as the mechanism behind Response.End ().
//
// On .NET Framework, Response.End () unwinds the request by aborting its thread with FlagEnd as the
// state object, and the pipeline catches ThreadAbortException, checks the state, and calls
// Thread.ResetAbort (). Neither half exists on .NET Core: Thread.Abort throws
// PlatformNotSupportedException ("Thread abort is not supported on this platform") and so does
// Thread.ResetAbort.
//
// That is not a corner case. Response.End () is what Server.Transfer uses, and what the ordinary
// one-argument Response.Redirect (url) uses - so every redirect written the normal way died here.
//
// This exception carries the same state object the abort did, and exposes ExceptionState under the
// same name, so the pipeline's existing catch bodies work unchanged after a type swap
// (tools/port-patches.txt). ResetAbort () is a no-op that keeps those bodies compiling and reads
// honestly at the call site.
//
// KNOWN BEHAVIOURAL DIFFERENCE, and it is unavoidable without thread aborts: ThreadAbortException is
// special-cased by the runtime and re-raised at the end of any catch block that swallows it, whereas
// this is an ordinary exception. Application code shaped like
//
//     try { Response.End (); } catch (Exception) { /* ignore */ }
//
// therefore swallows the end of the request instead of being unwound past. Every thread-abort-free
// implementation of Response.End has this property, including ASP.NET Core's own compatibility
// shims. Code that needs to intercept exceptions around Response.End should rethrow anything it does
// not recognise.
//

using System;

namespace System.Web.Util
{
	sealed class RequestEndException : Exception
	{
		internal RequestEndException (object exceptionState)
			: base ("The request was ended by Response.End ().")
		{
			ExceptionState = exceptionState;
		}

		/// <summary>
		/// The state object Thread.Abort would have carried - FlagEnd.Value for Response.End (), which
		/// is what the pipeline compares against to tell an intentional end from a real failure.
		/// </summary>
		internal object ExceptionState { get; private set; }

		/// <summary>
		/// Stands in for Thread.ResetAbort () at the sites that used to clear a pending abort. There is
		/// no pending abort to clear: catching this exception already stopped the unwind.
		/// </summary>
		internal static void ResetAbort ()
		{
		}
	}
}
