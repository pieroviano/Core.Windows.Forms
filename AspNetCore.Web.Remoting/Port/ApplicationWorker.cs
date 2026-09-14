//
// The child side of a hosted application: runs one forwarded request through the WebForms pipeline.
//
// The request is rebuilt as an ASP.NET Core HttpContext from features, and the ordinary UseWebForms
// middleware runs against it - so AspNetCoreWorkerRequest, which turns a Core request into System.Web's
// HttpWorkerRequest, is exactly the code a single-application Kestrel host runs. Nothing about header
// mapping, path splitting or body handling is reimplemented here, which is the point.
//
// Buffered in both directions: remoting is a call and a reply. A request body is read completely by the
// parent before forwarding, and the response is collected completely before it goes back.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Web.Hosting.Kestrel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace System.Web.Hosting
{
	/// <summary>A request as the parent received it. Crosses the process boundary.</summary>
	[Serializable]
	public sealed class ForwardedRequest
	{
		public string Method;
		public string Scheme;
		public string Protocol;
		public string PathBase;
		public string Path;
		public string QueryString;
		public string RawTarget;
		public string [] HeaderNames;
		public string [] [] HeaderValues;
		public string RemoteIpAddress;
		public int RemotePort;
		public string LocalIpAddress;
		public int LocalPort;
		public string TraceIdentifier;
		public byte [] Body;
	}

	/// <summary>The application's complete response. Crosses the process boundary.</summary>
	[Serializable]
	public sealed class ForwardedResponse
	{
		/// <summary>
		/// The domain is unloading and did not process the request, so it is safe to send elsewhere.
		/// </summary>
		public bool Refused;

		public int StatusCode;
		public string ReasonPhrase;
		public string [] HeaderNames;
		public string [] [] HeaderValues;
		public byte [] Body;
	}

	public interface IApplicationWorker
	{
		ForwardedResponse Process (ForwardedRequest request);
	}

	public class ApplicationWorker : MarshalByRefObject, IApplicationWorker
	{
		readonly object sync = new object ();
		IServiceProvider services;
		RequestDelegate pipeline;

		public virtual ForwardedResponse Process (ForwardedRequest request)
		{
			if (request == null)
				throw new ArgumentNullException (nameof (request));

			// HttpRuntime marks the domain as unloading and from then on drops requests without completing
			// them. Answering "refused" instead lets the parent resend to the domain replacing this one; the
			// request has not been touched, so that is safe for any method.
			if (HttpRuntime.DomainUnloading)
				return new ForwardedResponse { Refused = true };

			RequestDelegate run = Pipeline ();

			var features = new FeatureCollection ();

			var requestFeature = new HttpRequestFeature {
				Method = request.Method,
				Scheme = request.Scheme,
				Protocol = request.Protocol,
				PathBase = request.PathBase ?? String.Empty,
				Path = request.Path ?? "/",
				QueryString = request.QueryString ?? String.Empty,
				RawTarget = request.RawTarget,
				Body = new MemoryStream (request.Body ?? new byte [0], writable: false),
			};
			for (int i = 0; i < request.HeaderNames.Length; i++)
				requestFeature.Headers [request.HeaderNames [i]] = new StringValues (request.HeaderValues [i]);
			features.Set<IHttpRequestFeature> (requestFeature);

			var responseFeature = new HttpResponseFeature ();
			var responseBody = new MemoryStream ();
			features.Set<IHttpResponseFeature> (responseFeature);
			features.Set<IHttpResponseBodyFeature> (new StreamResponseBodyFeature (responseBody));

			features.Set<IHttpConnectionFeature> (new HttpConnectionFeature {
				RemoteIpAddress = ParseAddress (request.RemoteIpAddress),
				RemotePort = request.RemotePort,
				LocalIpAddress = ParseAddress (request.LocalIpAddress),
				LocalPort = request.LocalPort,
			});
			features.Set<IHttpRequestIdentifierFeature> (new HttpRequestIdentifierFeature { TraceIdentifier = request.TraceIdentifier });
			features.Set<IHttpRequestLifetimeFeature> (new HttpRequestLifetimeFeature ());
			features.Set<IServiceProvidersFeature> (new ServiceProvidersFeature { RequestServices = services });

			var context = new DefaultHttpContext (features);
			run (context).GetAwaiter ().GetResult ();

			var names = new List<string> ();
			var values = new List<string []> ();
			foreach (KeyValuePair<string, StringValues> header in responseFeature.Headers) {
				names.Add (header.Key);
				values.Add (header.Value.ToArray ());
			}

			return new ForwardedResponse {
				StatusCode = responseFeature.StatusCode,
				ReasonPhrase = responseFeature.ReasonPhrase,
				HeaderNames = names.ToArray (),
				HeaderValues = values.ToArray (),
				Body = responseBody.ToArray (),
			};
		}

		// Built on first use rather than in the constructor: the runtime is initialised by
		// ApplicationDomainInitializer, which runs after this object may already exist.
		RequestDelegate Pipeline ()
		{
			lock (sync) {
				if (pipeline != null)
					return pipeline;

				services = new ServiceCollection ().AddLogging ().BuildServiceProvider ();
				var builder = new ApplicationBuilder (services);

				// WebFormsRuntimeHost is already initialised for this domain, so UseWebForms only adds the
				// middleware. Everything it would otherwise (re)apply is handed back unchanged.
				builder.UseWebForms (options => {
					options.PhysicalPath = WebFormsRuntimeHost.PhysicalPath;
					options.VirtualPath = WebFormsRuntimeHost.VirtualPath;
					options.StateSerializer = WebFormsRuntimeHost.StateSerializer;
					options.ApplicationAssemblies = WebFormsRuntimeHost.ApplicationAssemblies;
					options.MachineConfigPath = WebFormsRuntimeHost.MachineConfigOverride;
					options.TemporaryFilesPath = WebFormsRuntimeHost.TemporaryFilesPath;
				});
				builder.Run (context => {
					context.Response.StatusCode = 404;
					return System.Threading.Tasks.Task.CompletedTask;
				});

				return pipeline = builder.Build ();
			}
		}

		static IPAddress ParseAddress (string text)
		{
			IPAddress address;
			return text != null && IPAddress.TryParse (text, out address) ? address : null;
		}
	}
}
