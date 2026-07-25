//
// ASP.NET Core middleware that hands a request to the ported System.Web pipeline.
//
// Threading model (plan P7, a deliberate v1 tradeoff):
//
//   HttpRuntime.ProcessRequest runs the whole HttpApplication pipeline *synchronously* on the
//   calling thread - IHttpHandler.ProcessRequest, the page lifecycle, viewstate, rendering. It is
//   an IAsyncResult-based state machine internally and converting it to Task is out of scope for
//   v1, so this middleware runs it on a thread-pool thread and awaits completion. That costs one
//   pooled thread per in-flight request, which is acceptable for a lift-and-shift and is the first
//   thing to revisit for throughput.
//
//   Rather than set AllowSynchronousIO on the server, the request body is buffered before the
//   pipeline starts and response bytes are buffered by AspNetCoreWorkerRequest and flushed
//   asynchronously afterwards. Kestrel's synchronous-IO guard is therefore never tripped.
//

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AspNetCoreHttpContext = Microsoft.AspNetCore.Http.HttpContext;

namespace System.Web.Hosting.Kestrel
{
	sealed class WebFormsMiddleware
	{
		readonly RequestDelegate next;
		readonly ILogger<WebFormsMiddleware> logger;
		readonly WebFormsOptions options;

		public WebFormsMiddleware (RequestDelegate next, WebFormsOptions options,
					   ILogger<WebFormsMiddleware> logger)
		{
			this.next = next ?? throw new ArgumentNullException (nameof (next));
			this.options = options ?? throw new ArgumentNullException (nameof (options));
			this.logger = logger;
		}

		public async Task InvokeAsync (AspNetCoreHttpContext context)
		{
			if (!ShouldHandle (context)) {
				await next (context).ConfigureAwait (false);
				return;
			}

			byte [] body = await ReadRequestBodyAsync (context).ConfigureAwait (false);

			var worker = new AspNetCoreWorkerRequest (
				context,
				WebFormsRuntimeHost.VirtualPath,
				WebFormsRuntimeHost.PhysicalPath,
				body);

			// HttpRuntime.BeginProcessRequest (plan P7) rather than ProcessRequest: the synchronous
			// entry blocks its thread for the entire request - HttpApplication does
			// "Start (null); done.WaitOne ()" - including across every point where the pipeline itself
			// goes async (an async module, an IHttpAsyncHandler, Page.RegisterAsyncTask). The async
			// entry returns immediately and the pipeline releases its thread at each yield, so no
			// thread is pinned for the duration of a request.
			Task pipeline = ProcessAsync (worker);

			try {
				await worker.Completion.ConfigureAwait (false);
			} catch (Exception e) when (!IsClientGone (e, context)) {
				logger?.LogError (e, "Unhandled exception processing {Path}", context.Request.Path);
				if (!context.Response.HasStarted) {
					context.Response.Clear ();
					context.Response.StatusCode = 500;
				}
				throw;
			} finally {
				// The flush cannot be allowed to throw.
				//
				// Two reasons, and the second is the sharper one. A client that has gone away - the back
				// button, a closed tab, a reload - signals RequestAborted, and every write below it then
				// throws OperationCanceledException. That is an ordinary event on a real site, not a
				// fault, and letting it out fills the log with cancellation stacks that read like errors.
				//
				// And because this is a finally, an exception thrown here REPLACES the one in flight. A
				// request that failed for a real reason and whose client then disconnected would report
				// the cancellation and lose the actual fault - the one the catch above just took the
				// trouble to log and turn into a 500. That is the worst possible trade: the more
				// interesting exception is the one discarded.
				try {
					await worker.FlushAsync (context.RequestAborted).ConfigureAwait (false);
				} catch (Exception e) when (IsClientGone (e, context)) {
					logger?.LogDebug ("Client disconnected before the response to {Path} was written",
							  context.Request.Path);
				}
			}

			// Surfaces failures thrown after EndOfRequest rather than swallowing them.
			await pipeline.ConfigureAwait (false);
		}

		// Wraps HttpRuntime's Begin/End pair as a Task. Failures are routed into the worker request's
		// completion so the awaiting middleware sees them even if they happen before EndOfRequest.
		static Task ProcessAsync (AspNetCoreWorkerRequest worker)
		{
			var tcs = new TaskCompletionSource<object> (TaskCreationOptions.RunContinuationsAsynchronously);

			try {
				HttpRuntime.BeginProcessRequest (worker, ar => {
					try {
						HttpRuntime.EndProcessRequest (ar);
						// A handler that returns without touching the response never calls
						// EndOfRequest; completing it here keeps such a request from hanging.
						worker.EndOfRequest ();
						tcs.TrySetResult (null);
					} catch (Exception e) {
						worker.AbortCompletion (e);
						tcs.TrySetException (e);
					}
				}, null);
			} catch (Exception e) {
				worker.AbortCompletion (e);
				tcs.TrySetException (e);
			}

			return tcs.Task;
		}

		// True when this exception is the client having gone away rather than the application failing.
		//
		// Both halves are needed. The TYPE alone is not enough: application code can throw
		// OperationCanceledException for its own reasons, and swallowing that would hide a real bug. The
		// TOKEN alone is not enough either, because a request can be aborted a moment after a genuine
		// failure. Requiring both means only a cancellation that the abort actually explains is treated
		// as one.
		static bool IsClientGone (Exception e, AspNetCoreHttpContext context)
		{
			if (!context.RequestAborted.IsCancellationRequested)
				return false;

			for (Exception current = e; current != null; current = current.InnerException)
				if (current is OperationCanceledException)
					return true;

			return false;
		}

		bool ShouldHandle (AspNetCoreHttpContext context)
		{
			if (options.ShouldHandle != null)
				return options.ShouldHandle (context);

			// Default: everything. System.Web's own <httpHandlers> decides what to do with a
			// path, including returning 403 for *.cs/*.config and 404 for unmapped *.axd, which
			// is behaviour applications depend on. Static files are best handled by putting
			// UseStaticFiles() ahead of UseWebForms().
			return true;
		}

		static async Task<byte []> ReadRequestBodyAsync (AspNetCoreHttpContext context)
		{
			// HttpRequest reads the entity body synchronously; buffer it here so the worker
			// request can serve it without synchronous IO on Kestrel's stream.
			if (context.Request.ContentLength == 0)
				return Array.Empty<byte> ();

			if (!context.Request.Body.CanRead)
				return Array.Empty<byte> ();

			using (var buffer = new MemoryStream ()) {
				await context.Request.Body.CopyToAsync (buffer, 64 * 1024, context.RequestAborted)
					.ConfigureAwait (false);
				return buffer.ToArray ();
			}
		}
	}
}
