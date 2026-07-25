//
// HttpWorkerRequest implemented over an ASP.NET Core HttpContext.
//
// This is the whole bridge between Kestrel and the ported System.Web: HttpRuntime.ProcessRequest
// takes an HttpWorkerRequest and the rest of the pipeline - HttpRequest, HttpResponse,
// HttpApplication, the page lifecycle - reads and writes exclusively through it. Implementing this
// one class is why none of those had to be touched.
//
// Response buffering: SendResponseFromMemory and friends are synchronous, and Kestrel's response
// body is async-only unless AllowSynchronousIO is set. Rather than force synchronous IO on the
// server, bytes accumulate here and are flushed asynchronously by FlushAsync, which the middleware
// calls after the pipeline finishes. Trailing writes after the final flush (rare, but possible
// through HttpResponse.Flush) are pushed straight out synchronously - the middleware enables
// AllowSynchronousIO for exactly that window.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using AspNetCoreHttpContext = Microsoft.AspNetCore.Http.HttpContext;

namespace System.Web.Hosting.Kestrel
{
	sealed class AspNetCoreWorkerRequest : HttpWorkerRequest
	{
		readonly AspNetCoreHttpContext core;
		readonly string appVirtualPath;
		readonly string appPhysicalPath;

		// Response state accumulated synchronously, flushed asynchronously.
		readonly MemoryStream buffer = new MemoryStream ();
		readonly List<KeyValuePair<string, string>> pendingHeaders = new List<KeyValuePair<string, string>> ();
		int statusCode = 200;
		string statusDescription;
		bool headersSent;
		bool endOfRequest;

		// Request body, read synchronously by HttpRequest. Kestrel gives us an async stream, so the
		// middleware pre-buffers it before the pipeline runs (see WebFormsMiddleware) and hands the
		// bytes over here.
		readonly byte [] entityBody;
		int entityBodyPosition;

		// Lazily resolved by ResolveFilePath; filePath being null means "not yet computed".
		string filePath;
		string pathInfo;

		readonly TaskCompletionSource<object> completed =
			new TaskCompletionSource<object> (TaskCreationOptions.RunContinuationsAsynchronously);

		internal AspNetCoreWorkerRequest (AspNetCoreHttpContext core, string appVirtualPath,
						  string appPhysicalPath, byte [] entityBody)
		{
			this.core = core ?? throw new ArgumentNullException (nameof (core));
			this.appVirtualPath = String.IsNullOrEmpty (appVirtualPath) ? "/" : appVirtualPath;
			this.appPhysicalPath = appPhysicalPath;
			this.entityBody = entityBody ?? Array.Empty<byte> ();
		}

		// Completes when the pipeline calls EndOfRequest.
		internal Task Completion {
			get { return completed.Task; }
		}

		/// <summary>
		/// The ASP.NET Core context this request is being served from.
		/// </summary>
		/// <remarks>
		/// The only route from inside the System.Web pipeline to what the ASP.NET Core side of the
		/// request knows - authentication being the case that matters. Internal: the public way in is
		/// WebFormsRuntimeHost.CurrentCoreContext, which does not expose this type.
		/// </remarks>
		internal AspNetCoreHttpContext CoreContext {
			get { return core; }
		}

		#region request line

		public override string GetHttpVerbName ()
		{
			return core.Request.Method;
		}

		public override string GetHttpVersion ()
		{
			string protocol = core.Request.Protocol;
			return String.IsNullOrEmpty (protocol) ? "HTTP/1.1" : protocol;
		}

		public override string GetProtocol ()
		{
			return core.Request.IsHttps ? "HTTPS" : "HTTP";
		}

		public override bool IsSecure ()
		{
			return core.Request.IsHttps;
		}

		// Path only, already URL-decoded by Kestrel. System.Web expects the decoded form here.
		public override string GetUriPath ()
		{
			string path = core.Request.Path.HasValue ? core.Request.Path.Value : "/";
			return String.IsNullOrEmpty (path) ? "/" : path;
		}

		public override string GetQueryString ()
		{
			string qs = core.Request.QueryString.HasValue ? core.Request.QueryString.Value : String.Empty;
			// QueryString.Value keeps the leading '?'; HttpWorkerRequest must not.
			if (qs.Length > 0 && qs [0] == '?')
				return qs.Substring (1);
			return qs;
		}

		public override byte [] GetQueryStringRawBytes ()
		{
			string qs = GetQueryString ();
			return qs.Length == 0 ? Array.Empty<byte> () : Encoding.UTF8.GetBytes (qs);
		}

		// The raw, still-encoded url. Kestrel keeps it in IHttpRequestFeature.RawTarget.
		public override string GetRawUrl ()
		{
			var feature = core.Features.Get<IHttpRequestFeature> ();
			string raw = feature != null ? feature.RawTarget : null;
			if (!String.IsNullOrEmpty (raw))
				return raw;

			string path = GetUriPath ();
			string qs = GetQueryString ();
			return qs.Length == 0 ? path : path + "?" + qs;
		}

		public override string GetFilePath ()
		{
			ResolveFilePath ();
			return filePath;
		}

		public override string GetPathInfo ()
		{
			ResolveFilePath ();
			return pathInfo;
		}

		// Splitting the request path into the file that handles it and the trailing PathInfo is the
		// WORKER REQUEST's job - under IIS the server does it before ASP.NET ever sees the request,
		// and nothing inside System.Web recomputes it (HttpRequest.FilePath/PathInfo just forward to
		// GetFilePath/GetPathInfo). Kestrel has no such notion, so it has to happen here.
		//
		// Returning the whole path as FilePath with an empty PathInfo - which is what this did at
		// first - breaks every feature built on PathInfo, because handler mapping is by extension and
		// a path ending in "/Echo" matches no extension at all. It falls through to the catch-all "*"
		// entry and DefaultHttpHandler answers "Method 'POST' is not allowed". That silently disables:
		//
		//   /Page.aspx/MethodName   page methods (ScriptModule dispatches on PathInfo)
		//   /Service.asmx/js        the script-service client proxy
		//   /Service.asmx/OpName    HTTP GET/POST (non-SOAP) web service invocation
		//   /Handler.ashx/anything  the common "REST-ish .ashx" pattern
		//
		// The split walks segment boundaries left to right - left to right, not right to left,
		// because for /a.aspx/b.aspx the handler is a.aspx, and IIS agrees - and stops at the first
		// boundary that looks like an endpoint:
		//
		//   the prefix is an existing FILE    definitive: /PageMethods.aspx/Echo
		//   the prefix has an EXTENSION but
		//     does not exist on disk          a virtual endpoint. This case is not optional:
		//                                     /Authentication_JSON_AppService.axd/Login is served
		//                                     entirely from config and has no file behind it, and
		//                                     so does every other *.axd. IIS behaves the same way -
		//                                     it splits on the script map, and a script-mapped
		//                                     extension is dispatched whether or not the file is
		//                                     really there.
		//   the prefix is an existing
		//     DIRECTORY                       keep walking. This is what keeps a directory whose
		//                                     name contains a dot (/v1.0/page.aspx) from being
		//                                     mistaken for a virtual endpoint.
		//
		// The common case - a path with no PathInfo - costs one filesystem probe, and the answer is
		// memoised for the life of the request because HttpRequest asks repeatedly.
		void ResolveFilePath ()
		{
			if (filePath != null)
				return;

			string uriPath = GetUriPath ();
			pathInfo = String.Empty;

			// The whole path is a real file or directory: no PathInfo, and no walk needed.
			bool isFile;
			if (Exists (uriPath, out isFile)) {
				filePath = uriPath;
				return;
			}

			// Start past the leading slash so the empty prefix is never probed.
			for (int i = uriPath.IndexOf ('/', 1); i > 0; i = uriPath.IndexOf ('/', i + 1)) {
				string candidate = uriPath.Substring (0, i);

				bool split;
				if (Exists (candidate, out isFile))
					split = isFile;                                 // file yes, directory no
				else
					split = HasExtension (candidate);

				if (split) {
					filePath = candidate;
					pathInfo = uriPath.Substring (i);
					return;
				}
			}

			// Nothing on the path looks like an endpoint - a 404, or a directory request. Handing
			// back the full path is what the rest of the pipeline (default documents, custom errors)
			// expects.
			filePath = uriPath;
		}

		// An extension on the LAST segment only. Path.GetExtension would answer "/v1.0/foo" with
		// ".0/foo", which would split in the middle of a directory name.
		static bool HasExtension (string virtualPath)
		{
			int slash = virtualPath.LastIndexOf ('/');
			int dot = virtualPath.LastIndexOf ('.');
			return dot > slash + 1 && dot < virtualPath.Length - 1;
		}

		bool Exists (string virtualPath, out bool isFile)
		{
			isFile = false;
			string physical;
			try {
				physical = MapPath (virtualPath);
			} catch (HttpException) {
				// MapPath refuses paths that escape the application root; such a path resolves to
				// no file, which is exactly what the caller needs to know.
				return false;
			}

			if (String.IsNullOrEmpty (physical))
				return false;

			if (File.Exists (physical)) {
				isFile = true;
				return true;
			}

			return Directory.Exists (physical);
		}

		#endregion

		#region application identity and paths

		public override string GetAppPath ()
		{
			return appVirtualPath;
		}

		public override string GetAppPathTranslated ()
		{
			return appPhysicalPath;
		}

		public override string GetFilePathTranslated ()
		{
			return MapPath (GetFilePath ());
		}

		public override string GetAppPoolID ()
		{
			return HttpRuntime.AppDomainAppId;
		}

		public override string MachineConfigPath {
			get { return WebFormsRuntimeHost.MachineConfigPath; }
		}

		public override string RootWebConfigPath {
			get { return WebFormsRuntimeHost.RootWebConfigPath; }
		}

		public override string MachineInstallDirectory {
			get { return AppContext.BaseDirectory; }
		}

		public override string MapPath (string virtualPath)
		{
			return WebFormsRuntimeHost.MapPath (virtualPath);
		}

		#endregion

		#region connection

		public override string GetLocalAddress ()
		{
			var addr = core.Connection.LocalIpAddress;
			return addr != null ? addr.ToString () : "127.0.0.1";
		}

		public override int GetLocalPort ()
		{
			return core.Connection.LocalPort;
		}

		public override string GetRemoteAddress ()
		{
			var addr = core.Connection.RemoteIpAddress;
			return addr != null ? addr.ToString () : "127.0.0.1";
		}

		public override int GetRemotePort ()
		{
			return core.Connection.RemotePort;
		}

		public override string GetRemoteName ()
		{
			return GetRemoteAddress ();
		}

		public override string GetServerName ()
		{
			string host = core.Request.Host.Host;
			return String.IsNullOrEmpty (host) ? GetLocalAddress () : host;
		}

		public override bool IsClientConnected ()
		{
			return !core.RequestAborted.IsCancellationRequested;
		}

		public override void CloseConnection ()
		{
			// Kestrel owns connection lifetime; the request completing is the signal it needs.
		}

		public override Guid RequestTraceIdentifier {
			get {
				Guid parsed;
				if (Guid.TryParse (core.TraceIdentifier, out parsed))
					return parsed;
				return Guid.Empty;
			}
		}

		#endregion

		#region request headers

		public override string GetKnownRequestHeader (int index)
		{
			string name = GetKnownRequestHeaderName (index);
			if (String.IsNullOrEmpty (name))
				return null;
			return GetUnknownRequestHeader (name);
		}

		public override string GetUnknownRequestHeader (string name)
		{
			StringValues values;
			if (!core.Request.Headers.TryGetValue (name, out values))
				return null;
			if (values.Count == 0)
				return null;
			// Multiple values are comma-joined, matching how ISAPI presented repeated headers.
			return values.Count == 1 ? values [0] : String.Join (",", values.ToArray ());
		}

		public override string [][] GetUnknownRequestHeaders ()
		{
			var result = new List<string []> ();
			foreach (var header in core.Request.Headers) {
				// "Unknown" means: not one of the RequestHeaderMaximum well-known names, because
				// those are served through GetKnownRequestHeader by index.
				if (IsKnownRequestHeaderName (header.Key))
					continue;
				result.Add (new [] { header.Key, header.Value.Count == 1
					? header.Value [0]
					: String.Join (",", header.Value.ToArray ()) });
			}
			return result.ToArray ();
		}

		static Dictionary<string, int> known_request_headers;

		static bool IsKnownRequestHeaderName (string name)
		{
			Dictionary<string, int> map = known_request_headers;
			if (map == null) {
				map = new Dictionary<string, int> (RequestHeaderMaximum, StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < RequestHeaderMaximum; i++) {
					string n = GetKnownRequestHeaderName (i);
					if (!String.IsNullOrEmpty (n))
						map [n] = i;
				}
				known_request_headers = map;
			}
			return map.ContainsKey (name);
		}

		public override string GetServerVariable (string name)
		{
			if (String.IsNullOrEmpty (name))
				return String.Empty;

			switch (name) {
			case "HTTPS":
				return core.Request.IsHttps ? "on" : "off";
			case "SERVER_NAME":
				return GetServerName ();
			case "SERVER_PORT":
				return GetLocalPort ().ToString (CultureInfo.InvariantCulture);
			case "SERVER_PROTOCOL":
				return GetHttpVersion ();
			case "SERVER_SOFTWARE":
				return "Kestrel";
			// Kestrel owns transfer framing, and this is how System.Web is told so.
			//
			// HttpResponse's constructor turns on its OWN chunked encoding for HTTP/1.1 whenever
			// GATEWAY_INTERFACE is absent or does not begin with "cgi" - correct for XSP, where the
			// worker request is a raw socket and somebody has to write the chunk headers. Here it means
			// System.Web writes "300000\r\n" and a terminating "0\r\n\r\n" INTO the body, and Kestrel
			// then frames that again: the client decodes one layer and is left with the other, so the
			// payload arrives corrupted by its own framing. A 3 MB download came back as 3,150,368 bytes.
			//
			// Reporting a CGI gateway is the switch upstream already provides for exactly this - a host
			// where transfer encoding is somebody else's job. It is not a claim to be CGI: nothing else
			// in System.Web reads this variable.
			case "GATEWAY_INTERFACE":
				return "CGI/1.1";
			case "REQUEST_METHOD":
				return GetHttpVerbName ();
			case "REMOTE_ADDR":
				return GetRemoteAddress ();
			case "REMOTE_HOST":
				return GetRemoteName ();
			case "REMOTE_PORT":
				return GetRemotePort ().ToString (CultureInfo.InvariantCulture);
			case "LOCAL_ADDR":
				return GetLocalAddress ();
			case "QUERY_STRING":
				return GetQueryString ();
			case "SCRIPT_NAME":
			case "URL":
				return GetFilePath ();
			case "PATH_INFO":
				return GetPathInfo ();
			case "PATH_TRANSLATED":
				return GetFilePathTranslated ();
			case "APPL_PHYSICAL_PATH":
				return GetAppPathTranslated ();
			case "APPL_MD_PATH":
				return GetAppPath ();
			}

			// HTTP_XXX_YYY maps to the request header Xxx-Yyy, as ISAPI did.
			if (name.StartsWith ("HTTP_", StringComparison.Ordinal)) {
				string header = name.Substring (5).Replace ('_', '-');
				return GetUnknownRequestHeader (header) ?? String.Empty;
			}

			return String.Empty;
		}

		#endregion

		#region request body

		public override bool IsEntireEntityBodyIsPreloaded ()
		{
			// The middleware buffers the whole body before the pipeline runs, so it always is.
			return true;
		}

		public override byte [] GetPreloadedEntityBody ()
		{
			return entityBody.Length == 0 ? null : entityBody;
		}

		public override int GetPreloadedEntityBody (byte [] buffer, int offset)
		{
			if (buffer == null)
				throw new ArgumentNullException (nameof (buffer));
			int count = Math.Min (entityBody.Length, buffer.Length - offset);
			if (count <= 0)
				return 0;
			Buffer.BlockCopy (entityBody, 0, buffer, offset, count);
			return count;
		}

		public override int GetPreloadedEntityBodyLength ()
		{
			return entityBody.Length;
		}

		public override int GetTotalEntityBodyLength ()
		{
			return entityBody.Length;
		}

		public override int ReadEntityBody (byte [] buffer, int size)
		{
			return ReadEntityBody (buffer, 0, size);
		}

		public override int ReadEntityBody (byte [] buffer, int offset, int size)
		{
			if (buffer == null)
				throw new ArgumentNullException (nameof (buffer));
			int available = entityBody.Length - entityBodyPosition;
			int count = Math.Min (available, Math.Min (size, buffer.Length - offset));
			if (count <= 0)
				return 0;
			Buffer.BlockCopy (entityBody, entityBodyPosition, buffer, offset, count);
			entityBodyPosition += count;
			return count;
		}

		public override long GetBytesRead ()
		{
			return entityBodyPosition;
		}

		#endregion

		#region response

		public override void SendStatus (int statusCode, string statusDescription)
		{
			this.statusCode = statusCode;
			this.statusDescription = statusDescription;
		}

		public override void SendKnownResponseHeader (int index, string value)
		{
			string name = GetKnownResponseHeaderName (index);
			if (String.IsNullOrEmpty (name))
				return;
			SendUnknownResponseHeader (name, value);
		}

		public override void SendUnknownResponseHeader (string name, string value)
		{
			if (String.IsNullOrEmpty (name))
				return;
			if (headersSent)
				// Too late to matter; dropping is better than throwing mid-render.
				return;
			pendingHeaders.Add (new KeyValuePair<string, string> (name, value ?? String.Empty));
		}

		public override bool HeadersSent ()
		{
			return headersSent;
		}

		public override void SendResponseFromMemory (byte [] data, int length)
		{
			if (data == null || length <= 0)
				return;
			buffer.Write (data, 0, Math.Min (length, data.Length));
		}

		public override void SendResponseFromMemory (IntPtr data, int length)
		{
			if (data == IntPtr.Zero || length <= 0)
				return;
			byte [] managed = new byte [length];
			System.Runtime.InteropServices.Marshal.Copy (data, managed, 0, length);
			buffer.Write (managed, 0, length);
		}

		public override void SendResponseFromFile (string filename, long offset, long length)
		{
			if (String.IsNullOrEmpty (filename) || length <= 0)
				return;

			using (FileStream fs = File.OpenRead (filename)) {
				fs.Seek (offset, SeekOrigin.Begin);
				CopyRange (fs, length);
			}
		}

		public override void SendResponseFromFile (IntPtr handle, long offset, long length)
		{
			if (handle == IntPtr.Zero || length <= 0)
				return;

			// Wrapped without owning the handle: System.Web hands us a descriptor it still owns.
			using (var sfh = new Microsoft.Win32.SafeHandles.SafeFileHandle (handle, ownsHandle: false))
			using (var fs = new FileStream (sfh, FileAccess.Read)) {
				fs.Seek (offset, SeekOrigin.Begin);
				CopyRange (fs, length);
			}
		}

		// Streamed to the client in 64 KB chunks, NOT accumulated in the buffer.
		//
		// The buffer is a MemoryStream, so buffering a file send means holding the whole file in managed
		// memory, per concurrent request, on the large object heap - a 200 MB download served to ten
		// clients is 2 GB. TransmitFile exists on .NET Framework precisely so that does not happen, and
		// a lift-and-shift port that turns it into a memory bomb has changed the thing that mattered
		// most about it.
		//
		// Anything already buffered is flushed first, so the file lands after the markup that preceded
		// it rather than in front of it.
		void CopyRange (Stream source, long length)
		{
			FlushResponse (finalFlush: false);

			var bodyControl = core.Features.Get<IHttpBodyControlFeature> ();
			bool allowed = bodyControl == null || bodyControl.AllowSynchronousIO;

			if (bodyControl != null)
				bodyControl.AllowSynchronousIO = true;

			try {
				Stream body = core.Response.Body;
				byte [] chunk = new byte [Math.Min (length, 64 * 1024)];
				long remaining = length;

				while (remaining > 0) {
					int want = (int) Math.Min (remaining, chunk.Length);
					int read = source.Read (chunk, 0, want);
					if (read <= 0)
						break;
					body.Write (chunk, 0, read);
					remaining -= read;
				}

				body.Flush ();
				flushedThrough = true;
			} finally {
				if (bodyControl != null)
					bodyControl.AllowSynchronousIO = allowed;
			}
		}

		public override void SendCalculatedContentLength (int contentLength)
		{
			SendCalculatedContentLength ((long) contentLength);
		}

		public override void SendCalculatedContentLength (long contentLength)
		{
			if (!headersSent)
				core.Response.ContentLength = contentLength;
		}

		//
		// System.Web calls this synchronously whenever HttpResponse flushes, and once more with
		// finalFlush: true at the end of the request.
		//
		// It really flushes. That is worth stating because the obvious implementation - hold everything
		// until the middleware's async flush - is what this used to do, and it silently broke every
		// application that streams: progressive rendering, server-sent events, a long report written row
		// by row, or anything setting Response.BufferOutput = false. None of them errored. The page
		// still rendered, all at once, at the end, which is exactly why no test noticed: a test asserting
		// on a complete response body cannot tell the difference.
		//
		// Writing here means synchronous IO on the response body, which Kestrel forbids by default, so
		// the window is opened explicitly through IHttpBodyControlFeature and closed again. That is
		// narrow and deliberate: it lasts for one flush the application explicitly asked for, rather
		// than being switched on for the whole server.
		//
		public override void FlushResponse (bool finalFlush)
		{
			WriteHeaders ();

			if (buffer.Length == 0) {
				if (finalFlush)
					flushedThrough = true;
				return;
			}

			var bodyControl = core.Features.Get<IHttpBodyControlFeature> ();
			bool allowed = bodyControl == null || bodyControl.AllowSynchronousIO;

			if (bodyControl != null)
				bodyControl.AllowSynchronousIO = true;

			try {
				Stream body = core.Response.Body;
				buffer.Position = 0;
				buffer.CopyTo (body);
				buffer.SetLength (0);
				body.Flush ();
				flushedThrough = true;
			} finally {
				if (bodyControl != null)
					bodyControl.AllowSynchronousIO = allowed;
			}
		}

		// Set once anything has been written straight to the response body. The middleware uses it to
		// know that its own async flush has nothing left to do - not to skip it, which would leave the
		// last buffered bytes unsent.
		bool flushedThrough;

		// Called by the middleware once the synchronous pipeline has returned. Writes whatever the
		// pipeline left buffered - which for a page that never flushed is the whole response, and for
		// one that streamed is only the tail.
		internal async Task FlushAsync (CancellationToken cancellationToken)
		{
			WriteHeaders ();

			if (buffer.Length > 0) {
				buffer.Position = 0;
				await buffer.CopyToAsync (core.Response.Body, 64 * 1024, cancellationToken)
					.ConfigureAwait (false);
				buffer.SetLength (0);
			}

			// Skipped when nothing was ever written AND something already flushed through: flushing an
			// empty body a second time is harmless but pointless, and on an aborted request it is one
			// more chance to throw.
			if (!flushedThrough || buffer.Length > 0)
				await core.Response.Body.FlushAsync (cancellationToken).ConfigureAwait (false);
		}

		void WriteHeaders ()
		{
			if (headersSent)
				return;
			headersSent = true;

			if (!core.Response.HasStarted) {
				core.Response.StatusCode = statusCode;
				if (!String.IsNullOrEmpty (statusDescription)) {
					var reason = core.Features.Get<IHttpResponseFeature> ();
					if (reason != null)
						reason.ReasonPhrase = statusDescription;
				}

				foreach (var header in pendingHeaders) {
					// Content-Length is owned by the response object; letting a header write
					// through would fight with SendCalculatedContentLength and truncate output.
					if (String.Equals (header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
						continue;
					if (String.Equals (header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
						continue;

					if (core.Response.Headers.ContainsKey (header.Key))
						core.Response.Headers [header.Key] =
							StringValues.Concat (core.Response.Headers [header.Key], header.Value);
					else
						core.Response.Headers [header.Key] = header.Value;
				}
			}

			pendingHeaders.Clear ();
		}

		public override void EndOfRequest ()
		{
			if (endOfRequest)
				return;
			endOfRequest = true;
			completed.TrySetResult (null);
		}

		public override void SetEndOfSendNotification (EndOfSendNotification callback, object extraData)
		{
			// Upstream uses this only to release per-request resources; nothing extra is needed
			// because the middleware disposes what it allocated when the request completes.
		}

		#endregion

		internal void AbortCompletion (Exception e)
		{
			completed.TrySetException (e);
		}
	}
}
