//
// System.Runtime.Remoting.Channels.Http.HttpRemotingHandlerFactory - *.rem and *.soap in a web application.
//
// Root web.config maps both extensions here (Tools/gen-config.ps1), exactly as .NET Framework's did. The
// shape follows upstream's factory (mono/mcs/class/System.Runtime.Remoting/.../HttpRemotingHandlerFactory.cs):
// on the first request, apply web.config's <system.runtime.remoting> section, find the http channel
// that wants to be hooked (registering one if the configuration declared none), and give it the
// application's base url; then every request is handed to that channel.
//
// What differs is the channel underneath - Net4x.Runtime.Remoting's, not System.Runtime.Remoting's.
// Its hook takes the request body and url rather than exposing a byte-level sink chain, and it speaks
// only the binary formatter: a SOAP request (text/xml, what *.soap endpoints usually received) is
// answered 415 naming the format that is supported, rather than failing somewhere inside the
// deserializer. Both ends of a call must reference Net4x.Runtime.Remoting.
//

using System;
using System.IO;
using System.Linq;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Web;
using System.Web.Hosting;
using System.Xml.Linq;

namespace System.Runtime.Remoting.Channels.Http
{
	public class HttpRemotingHandlerFactory : IHttpHandlerFactory
	{
		const string BinaryContentType = "application/octet-stream";

		static readonly object sync = new object ();
		static IChannelReceiverHook hook;

		public IHttpHandler GetHandler (HttpContext context, string requestType, string url, string pathTranslated)
		{
			return new HttpRemotingHandler (EnsureHooked (context));
		}

		public void ReleaseHandler (IHttpHandler handler)
		{
		}

		static IChannelReceiverHook EnsureHooked (HttpContext context)
		{
			lock (sync) {
				if (hook != null)
					return hook;

				// Once per process, like upstream: the section registers channels and well-known types,
				// and registering either twice throws.
				string webConfig = ApplicationHost.FindWebConfig (HttpRuntime.AppDomainAppPath);
				if (webConfig != null && HasRemotingSection (webConfig))
					RemotingConfiguration.Configure (webConfig, false);

				IChannelReceiverHook found = null;
				foreach (IChannel channel in ChannelServices.RegisteredChannels) {
					var candidate = channel as IChannelReceiverHook;
					if (candidate == null)
						continue;

					if (!String.Equals (candidate.ChannelScheme, "http", StringComparison.OrdinalIgnoreCase))
						throw new RemotingException (
							"Only http channels are allowed when hosting remoting objects in a web server; " +
							"channel '" + channel.ChannelName + "' uses '" + candidate.ChannelScheme + "'.");

					if (candidate.WantsToListen) {
						found = candidate;
						break;
					}
				}

				if (found == null) {
					var channel = new HttpChannel ();
					ChannelServices.RegisterChannel (channel, false);
					found = channel;
				}

				// Scheme, authority and application path: objects published afterwards advertise urls
				// below it, so a reference handed to a client points back through this web server.
				Uri requestUrl = context.Request.Url;
				found.AddHookChannelUri (requestUrl.GetLeftPart (UriPartial.Authority) + context.Request.ApplicationPath);

				hook = found;
				return hook;
			}
		}

		// RemotingConfiguration.Configure refuses a file with no section, and most web.config files have
		// none: an application can publish its types in code (Application_Start) instead.
		static bool HasRemotingSection (string path)
		{
			XDocument document = XDocument.Load (path);
			return document.Root != null && document.Root.Elements ("system.runtime.remoting").Any ();
		}

		sealed class HttpRemotingHandler : IHttpHandler
		{
			readonly IChannelReceiverHook channel;

			public HttpRemotingHandler (IChannelReceiverHook channel)
			{
				this.channel = channel;
			}

			public bool IsReusable {
				get { return true; }
			}

			public void ProcessRequest (HttpContext context)
			{
				HttpRequest request = context.Request;
				HttpResponse response = context.Response;

				if (!String.Equals (request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)) {
					response.AppendHeader ("Allow", "POST");
					WriteText (response, 405, "A remoting endpoint accepts POST only.");
					return;
				}

				string mediaType = (request.ContentType ?? String.Empty).Split (';') [0].Trim ();
				if (mediaType.Length > 0 && !String.Equals (mediaType, BinaryContentType, StringComparison.OrdinalIgnoreCase)) {
					WriteText (response, 415,
						"This endpoint speaks the binary remoting format (" + BinaryContentType + "); '" +
						mediaType + "' is not supported. SOAP remoting has no implementation off .NET Framework, " +
						"and both ends of a call must reference Net4x.Runtime.Remoting.");
					return;
				}

				if (request.ContentLength > RemotingConfiguration.MaxFrameLength) {
					WriteText (response, 413,
						"The request body exceeds RemotingConfiguration.MaxFrameLength (" +
						RemotingConfiguration.MaxFrameLength + " bytes).");
					return;
				}

				byte [] body;
				using (var buffer = new MemoryStream ()) {
					request.InputStream.CopyTo (buffer);
					body = buffer.ToArray ();
				}

				byte [] reply = channel.ProcessHookedRequest (body, request.Url.AbsoluteUri);

				response.ContentType = BinaryContentType;
				response.OutputStream.Write (reply, 0, reply.Length);
			}

			static void WriteText (HttpResponse response, int status, string text)
			{
				response.StatusCode = status;
				response.ContentType = "text/plain";
				response.Write (text);
			}
		}
	}
}
