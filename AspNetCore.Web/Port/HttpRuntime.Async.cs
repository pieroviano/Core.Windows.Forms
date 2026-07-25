//
// Asynchronous request entry point (plan P7).
//
// HttpRuntime.ProcessRequest -> HttpApplication's IHttpHandler.ProcessRequest does:
//
//     Start (null);
//     done.WaitOne ();          // HttpApplication.cs
//
// i.e. it blocks the calling thread for the whole request - not just while the page renders, but
// across every point where the pipeline internally goes asynchronous (an async HttpModule, an
// IHttpAsyncHandler, Page.RegisterAsyncTask). Hosting that from Kestrel meant one thread-pool thread
// pinned per in-flight request.
//
// HttpApplication also implements IHttpAsyncHandler, whose BeginProcessRequest returns immediately
// and completes through a callback; the pipeline is an iterator (HttpApplication.Pipeline/Tick) that
// releases its thread at each async yield. Upstream never wired that up - HttpRuntime.Process has the
// async version sitting commented out - so this supplies it.
//
// Everything else mirrors Process() exactly: same first-run/offline handling, same
// HttpApplicationFactory acquisition and recycling, same end-of-send notification. It is a partial of
// HttpRuntime rather than a separate class because all of that is private (AppIsOffline,
// FinishWithException, end_of_send_cb, firstRun, initialException, SetupOfflineWatch).
//

using System;
using System.Threading;
using System.Web.Management;

namespace System.Web
{
	public sealed partial class HttpRuntime
	{
		/// <summary>
		/// Begins processing a request without blocking the calling thread. The returned
		/// IAsyncResult completes when the pipeline is finished; pass it to
		/// <see cref="EndProcessRequest"/>.
		/// </summary>
		public static IAsyncResult BeginProcessRequest (HttpWorkerRequest wr, AsyncCallback cb, object state)
		{
			if (wr == null)
				throw new ArgumentNullException ("wr");

			// Same first-run bookkeeping Process() does.
			if (firstRun) {
				firstRun = false;
				if (initialException != null) {
					FinishWithException (wr, HttpException.NewWithCode (
						"Initial exception", initialException, WebEventCodes.RuntimeErrorRequestAbort));
					return CompletedResult.Failed (cb, state);
				}
				SetupOfflineWatch ();
			}

			HttpContext context = new HttpContext (wr);
			HttpContext.Current = context;

			// App_Offline.htm: AppIsOffline writes the response itself and we are done.
			if (AppIsOffline (context))
				return CompletedResult.Succeeded (cb, state);

			HttpApplication app;
			try {
				app = HttpApplicationFactory.GetApplication (context);
			} catch (Exception e) {
				FinishWithException (wr, HttpException.NewWithCode (
					String.Empty, e, WebEventCodes.RuntimeErrorRequestAbort));
				context.Request.ReleaseResources ();
				context.Response.ReleaseResources ();
				HttpContext.Current = null;
				return CompletedResult.Failed (cb, state);
			}

			context.ApplicationInstance = app;
			wr.SetEndOfSendNotification (end_of_send_cb, context);

			var pending = new PendingRequest (app, context, cb, state);
			try {
				pending.Begin ();
			} catch (Exception) {
				pending.Cleanup ();
				throw;
			}
			return pending;
		}

		/// <summary>Completes a request started by <see cref="BeginProcessRequest"/>.</summary>
		public static void EndProcessRequest (IAsyncResult result)
		{
			if (result == null)
				throw new ArgumentNullException ("result");

			var pending = result as PendingRequest;
			if (pending != null) {
				pending.End ();
				return;
			}

			// CompletedResult - nothing to wait for.
			if (!result.IsCompleted)
				result.AsyncWaitHandle.WaitOne ();
		}

		//
		// Drives one request through HttpApplication's IHttpAsyncHandler and recycles the application
		// afterwards, exactly as Process() does after ProcessRequest returns.
		//
		sealed class PendingRequest : IAsyncResult
		{
			readonly HttpApplication app;
			readonly HttpContext context;
			readonly AsyncCallback callback;
			readonly object state;
			readonly ManualResetEvent completed = new ManualResetEvent (false);

			IAsyncResult inner;
			Exception error;
			int done;

			internal PendingRequest (HttpApplication app, HttpContext context, AsyncCallback cb, object state)
			{
				this.app = app;
				this.context = context;
				this.callback = cb;
				this.state = state;
			}

			internal void Begin ()
			{
				IHttpAsyncHandler handler = app;
				inner = handler.BeginProcessRequest (context, OnPipelineComplete, null);

				// A pipeline that finished synchronously still has to be completed exactly once;
				// OnPipelineComplete guards with Interlocked so both paths are safe.
				if (inner != null && inner.IsCompleted)
					OnPipelineComplete (inner);
			}

			void OnPipelineComplete (IAsyncResult ar)
			{
				if (Interlocked.Exchange (ref done, 1) != 0)
					return;

				try {
					((IHttpAsyncHandler) app).EndProcessRequest (ar);
				} catch (Exception e) {
					error = e;
				}

				Cleanup ();
				completed.Set ();

				if (callback != null) {
					try {
						callback (this);
					} catch {
						// A throwing completion callback must not take down the pipeline thread;
						// the host observes the failure through EndProcessRequest.
					}
				}
			}

			internal void Cleanup ()
			{
				try {
					HttpApplicationFactory.Recycle (app);
				} finally {
					HttpContext.Current = null;
				}
			}

			internal void End ()
			{
				if (!IsCompleted)
					completed.WaitOne ();
				if (error != null)
					throw error;
			}

			public object AsyncState {
				get { return state; }
			}

			public WaitHandle AsyncWaitHandle {
				get { return completed; }
			}

			public bool CompletedSynchronously {
				get { return false; }
			}

			public bool IsCompleted {
				get { return Thread.VolatileRead (ref done) != 0; }
			}
		}

		// For the paths that finish before the pipeline is ever entered (initial exception,
		// app offline, application creation failure).
		sealed class CompletedResult : IAsyncResult
		{
			readonly object state;
			readonly ManualResetEvent handle = new ManualResetEvent (true);

			CompletedResult (object state)
			{
				this.state = state;
			}

			internal static CompletedResult Succeeded (AsyncCallback cb, object state)
			{
				var r = new CompletedResult (state);
				if (cb != null)
					cb (r);
				return r;
			}

			internal static CompletedResult Failed (AsyncCallback cb, object state)
			{
				// Same shape as Succeeded: the response has already been written by the caller.
				return Succeeded (cb, state);
			}

			public object AsyncState {
				get { return state; }
			}

			public WaitHandle AsyncWaitHandle {
				get { return handle; }
			}

			public bool CompletedSynchronously {
				get { return true; }
			}

			public bool IsCompleted {
				get { return true; }
			}
		}
	}
}
