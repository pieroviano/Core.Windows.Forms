using System;
using System.Web;

namespace WebFormsSample
{
	// Registered in web.config <httpModules>. Real applications almost always have one of these for
	// logging, auth or header manipulation, so the registration path matters.
	public class TraceModule : IHttpModule
	{
		public void Init (HttpApplication app)
		{
			app.BeginRequest += (s, e) => {
				var ctx = ((HttpApplication) s).Context;
				ctx.Items ["module.begin"] = DateTime.UtcNow.Ticks;
				ctx.Response.AppendHeader ("X-Custom-Module", "begin");
			};
			app.EndRequest += (s, e) => {
				var ctx = ((HttpApplication) s).Context;
				if (ctx.Items ["module.begin"] == null)
					return;

				// Guarded, because a header cannot be added once the response has started - the status
				// line and headers are already on the wire. That is true on IIS too; it only appears
				// safe in EndRequest when the whole response is buffered to the end of the request, and
				// this port streams whatever the application flushes. A page that calls Response.Flush ()
				// - Streaming.aspx does - reaches here with headers long since sent.
				if (!ctx.Response.HeadersWritten)
					ctx.Response.AppendHeader ("X-Custom-Module-Saw-Items", "yes");
			};
		}

		public void Dispose ()
		{
		}
	}
}
