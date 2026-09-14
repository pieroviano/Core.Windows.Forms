//
// Several ported applications behind one Kestrel, each in its own child domain, recycled the way IIS
// recycled an application.
//
// What a single-application host cannot do and this can:
//
//   * two applications in one server - each child process has its own HttpRuntime, static state,
//     configuration and compiled pages, which is all a process allows (see LIMITATIONS.md);
//   * recycling - upstream's file watchers call HttpRuntime.UnloadAppDomain when bin, App_Code,
//     Global.asax or web.config change; in a child domain that reaches the parent (PortApplicationLifetime),
//     which replaces the process. HttpRuntime.UnloadAppDomain from application code does the same.
//
// Recycling is overlapped, as IIS's was: the retiring domain stops receiving requests at once, finishes
// the ones it has (up to ShutdownTimeout), and the next request starts its replacement. A request the
// retiring domain refused unprocessed is sent to the replacement. A domain that dies is replaced on the
// next request; a request it was serving is answered 503, never resent, because it may have had effects.
//
// What it costs: every request crosses a process boundary, buffered both ways - no streaming responses,
// no WebSockets, no ASP.NET Core authentication flowing into the application. See LIMITATIONS.md.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AspNetCoreHttpContext = Microsoft.AspNetCore.Http.HttpContext;

namespace System.Web.Hosting.Kestrel
{
	public sealed class WebFormsApplicationOptions
	{
		/// <summary>Physical directory of the application - the one containing web.config.</summary>
		public string PhysicalPath { get; set; }

		/// <summary>Virtual path the application is served under, such as "/sales".</summary>
		public string VirtualPath { get; set; }

		/// <summary>Site name reported through HostingEnvironment.SiteName.</summary>
		public string SiteName { get; set; } = "Kestrel";

		/// <summary>Compilation output directory. See WebFormsOptions.TemporaryFilesPath.</summary>
		public string TemporaryFilesPath { get; set; }

		/// <summary>A complete machine.config. See WebFormsOptions.MachineConfigPath.</summary>
		public string MachineConfigPath { get; set; }

		/// <summary>Assemblies to load in the domain before its first request. See WebFormsOptions.ApplicationAssemblies.</summary>
		public IEnumerable<Assembly> ApplicationAssemblies { get; set; }

		/// <summary>
		/// An IStateObjectSerializer with a parameterless constructor. A type rather than an instance: it is
		/// created inside the application's domain.
		/// </summary>
		public Type StateSerializerType { get; set; }

		/// <summary>Start the domain when the host starts and again right after each recycle.</summary>
		public bool PreloadEnabled { get; set; }

		/// <summary>How long a retiring domain may finish its requests before it is ended. IIS's default.</summary>
		public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds (90);

		/// <summary>Longest a single request may run in the domain.</summary>
		public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes (2);
	}

	/// <summary>The applications a host serves. Pass to UseWebFormsApplications.</summary>
	public sealed class WebFormsApplications : IDisposable
	{
		readonly List<WebFormsApplicationDomain> domains = new List<WebFormsApplicationDomain> ();

		public IReadOnlyList<WebFormsApplicationDomain> Domains => domains;

		public WebFormsApplicationDomain Add (string virtualPath, string physicalPath, Action<WebFormsApplicationOptions> configure = null)
		{
			var options = new WebFormsApplicationOptions { VirtualPath = virtualPath, PhysicalPath = physicalPath };
			configure?.Invoke (options);

			var domain = new WebFormsApplicationDomain (options);
			if (domains.Any (d => String.Equals (d.VirtualPath, domain.VirtualPath, StringComparison.OrdinalIgnoreCase)))
				throw new ArgumentException ("An application is already added at '" + domain.VirtualPath + "'.", nameof (virtualPath));

			domains.Add (domain);
			return domain;
		}

		/// <summary>The application whose virtual path is the longest whole-segment prefix of <paramref name="path"/>.</summary>
		public WebFormsApplicationDomain Find (string path)
		{
			WebFormsApplicationDomain best = null;
			foreach (WebFormsApplicationDomain domain in domains) {
				string root = domain.VirtualPath.TrimEnd ('/');
				bool matches = root.Length == 0 ||
					(path.StartsWith (root, StringComparison.OrdinalIgnoreCase) &&
					 (path.Length == root.Length || path [root.Length] == '/'));
				if (matches && (best == null || root.Length > best.VirtualPath.TrimEnd ('/').Length))
					best = domain;
			}
			return best;
		}

		public void Dispose ()
		{
			foreach (WebFormsApplicationDomain domain in domains)
				domain.Dispose ();
		}
	}

	/// <summary>One hosted application and the child domain currently serving it.</summary>
	public sealed class WebFormsApplicationDomain : IDisposable
	{
		readonly WebFormsApplicationOptions options;
		readonly object sync = new object ();
		Generation current;
		int generations;
		bool disposed;

		internal ILogger Logger;

		internal WebFormsApplicationOptions Options => options;

		internal WebFormsApplicationDomain (WebFormsApplicationOptions options)
		{
			if (String.IsNullOrEmpty (options.PhysicalPath))
				throw new ArgumentException ("PhysicalPath is required.", nameof (options));

			this.options = options;
			PhysicalPath = Path.GetFullPath (options.PhysicalPath);

			string vpath = String.IsNullOrEmpty (options.VirtualPath) ? "/" : options.VirtualPath;
			if (vpath [0] != '/')
				vpath = "/" + vpath;
			VirtualPath = vpath.Length > 1 ? vpath.TrimEnd ('/') : vpath;
		}

		public string VirtualPath { get; }

		public string PhysicalPath { get; }

		/// <summary>How many domains have been started for this application.</summary>
		public int Generations => Volatile.Read (ref generations);

		/// <summary>The process serving the application, or null when no domain is running.</summary>
		public int? ProcessId {
			get {
				Generation generation = current;
				return generation != null && generation.Started.IsCompletedSuccessfully && generation.Domain.IsAlive
					? generation.Domain.ProcessId
					: (int?) null;
			}
		}

		/// <summary>Replaces the domain: new requests go to a fresh one, the current one finishes and ends.</summary>
		public void Recycle ()
		{
			Generation generation;
			lock (sync)
				generation = current;
			if (generation != null)
				Retire (generation, "Recycle requested by the host");
		}

		/// <summary>Starts the domain if none is running.</summary>
		public Task StartAsync ()
		{
			Generation generation = Acquire ();
			return generation.Started.ContinueWith (t => Release (generation), TaskScheduler.Default);
		}

		internal async Task ProcessAsync (AspNetCoreHttpContext context)
		{
			ForwardedRequest request = await ReadRequestAsync (context).ConfigureAwait (false);

			// Twice at most: a domain can refuse a request only once it is retiring, and the next generation
			// is fresh.
			for (int attempt = 0; attempt < 2; attempt++) {
				Generation generation = Acquire ();
				ForwardedResponse response;
				try {
					try {
						await generation.Started.ConfigureAwait (false);
					} catch (Exception e) {
						Retire (generation, "The application domain failed to start");
						Logger?.LogError (e, "Application {VirtualPath} failed to start", VirtualPath);
						await WriteTextAsync (context, 500, "The application at " + VirtualPath + " failed to start: " + e.Message).ConfigureAwait (false);
						return;
					}

					try {
						response = await Task.Run (() => generation.Worker.Process (request)).ConfigureAwait (false);
					} catch (Exception e) when (!generation.Domain.IsAlive || e is AppDomainUnloadedException || e is RemotingConnectionException) {
						Retire (generation, "The application domain ended during a request");
						Logger?.LogError (e, "Application {VirtualPath} ended while serving {Path}", VirtualPath, context.Request.Path);
						await WriteTextAsync (context, 503, "The application at " + VirtualPath + " stopped while serving this request.").ConfigureAwait (false);
						return;
					}
				} finally {
					Release (generation);
				}

				if (response.Refused) {
					Retire (generation, "The application domain is unloading");
					continue;
				}

				await WriteResponseAsync (context, response).ConfigureAwait (false);
				return;
			}

			await WriteTextAsync (context, 503, "The application at " + VirtualPath + " is restarting.").ConfigureAwait (false);
		}

		Generation Acquire ()
		{
			lock (sync) {
				if (disposed)
					throw new ObjectDisposedException (nameof (WebFormsApplicationDomain));

				if (current == null || current.Retiring ||
				    (current.Started.IsCompleted && (current.Domain == null || !current.Domain.IsAlive)))
					current = StartGeneration ();

				current.InFlight++;
				return current;
			}
		}

		void Release (Generation generation)
		{
			bool unload;
			lock (sync) {
				generation.InFlight--;
				unload = generation.Retiring && generation.InFlight == 0;
			}
			if (unload)
				generation.Unload ();
		}

		void Retire (Generation generation, string reason)
		{
			bool unloadNow;
			bool preload;
			lock (sync) {
				if (generation.Retiring)
					return;
				generation.Retiring = true;
				if (current == generation)
					current = null;
				unloadNow = generation.InFlight == 0;
				preload = options.PreloadEnabled && !disposed;
			}

			Logger?.LogInformation ("Recycling application {VirtualPath}: {Reason}", VirtualPath, reason);

			if (unloadNow) {
				generation.Unload ();
			} else {
				// Requests still running get ShutdownTimeout to finish, then the domain ends regardless.
				Task.Delay (options.ShutdownTimeout).ContinueWith (_ => generation.Unload (), TaskScheduler.Default);
			}

			if (preload)
				_ = StartAsync ();
		}

		Generation StartGeneration ()
		{
			var generation = new Generation { Number = Interlocked.Increment (ref generations) };
			var events = new GenerationEvents (this, generation);

			var settings = new ApplicationDomainSettings {
				PhysicalPath = PhysicalPath,
				VirtualPath = VirtualPath,
				SiteName = options.SiteName,
				TemporaryFilesPath = options.TemporaryFilesPath,
				MachineConfigPath = options.MachineConfigPath,
				StateSerializerType = options.StateSerializerType?.AssemblyQualifiedName,
				StateServerProviderType = System.Web.SessionState.PortSessionState.StateServerProviderType,
				ApplicationAssemblyPaths = (options.ApplicationAssemblies ?? Enumerable.Empty<Assembly> ())
					.Where (a => a != null && !a.IsDynamic && a.Location.Length > 0)
					.Select (a => a.Location)
					.ToArray (),
			};

			// Off the request thread and outside the lock: a domain takes a moment to start, and requests
			// arriving meanwhile wait on this task rather than start domains of their own.
			generation.Started = Task.Run (() => {
				ChildAppDomain domain = ApplicationDomains.Start (settings, events, options.RequestTimeout);
				generation.Domain = domain;
				domain.DomainUnloaded += (sender, e) => Retire (generation, "The application domain process ended");
				generation.Worker = domain.CreateInstanceAndUnwrap<ApplicationWorker> ();
			});
			return generation;
		}

		static async Task<ForwardedRequest> ReadRequestAsync (AspNetCoreHttpContext context)
		{
			Microsoft.AspNetCore.Http.HttpRequest request = context.Request;

			byte [] body;
			using (var buffer = new MemoryStream ()) {
				await request.Body.CopyToAsync (buffer, 64 * 1024, context.RequestAborted).ConfigureAwait (false);
				body = buffer.ToArray ();
			}

			return new ForwardedRequest {
				Method = request.Method,
				Scheme = request.Scheme,
				Protocol = request.Protocol,
				PathBase = request.PathBase.Value,
				Path = request.Path.Value,
				QueryString = request.QueryString.Value,
				RawTarget = context.Features.Get<IHttpRequestFeature> ()?.RawTarget,
				HeaderNames = request.Headers.Keys.ToArray (),
				HeaderValues = request.Headers.Values.Select (v => v.ToArray ()).ToArray (),
				RemoteIpAddress = context.Connection.RemoteIpAddress?.ToString (),
				RemotePort = context.Connection.RemotePort,
				LocalIpAddress = context.Connection.LocalIpAddress?.ToString (),
				LocalPort = context.Connection.LocalPort,
				TraceIdentifier = context.TraceIdentifier,
				Body = body,
			};
		}

		// Framing is Kestrel's: the body goes out with the length it has, whatever the application wrote.
		static readonly HashSet<string> FramingHeaders = new HashSet<string> (StringComparer.OrdinalIgnoreCase) {
			"Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive",
		};

		static async Task WriteResponseAsync (AspNetCoreHttpContext context, ForwardedResponse response)
		{
			context.Response.StatusCode = response.StatusCode;
			if (!String.IsNullOrEmpty (response.ReasonPhrase))
				context.Features.Get<IHttpResponseFeature> ().ReasonPhrase = response.ReasonPhrase;

			for (int i = 0; i < response.HeaderNames.Length; i++) {
				if (!FramingHeaders.Contains (response.HeaderNames [i]))
					context.Response.Headers [response.HeaderNames [i]] = response.HeaderValues [i];
			}

			context.Response.ContentLength = response.Body.Length;
			await context.Response.Body.WriteAsync (response.Body, 0, response.Body.Length, context.RequestAborted).ConfigureAwait (false);
		}

		static Task WriteTextAsync (AspNetCoreHttpContext context, int status, string text)
		{
			context.Response.StatusCode = status;
			context.Response.ContentType = "text/plain; charset=utf-8";
			return context.Response.WriteAsync (text);
		}

		public void Dispose ()
		{
			Generation generation;
			lock (sync) {
				disposed = true;
				generation = current;
				current = null;
			}
			generation?.Unload ();
		}

		sealed class Generation
		{
			public int Number;
			public Task Started;
			public ChildAppDomain Domain;
			public IApplicationWorker Worker;
			public int InFlight;
			public bool Retiring;
			int unloaded;

			public void Unload ()
			{
				if (Interlocked.Exchange (ref unloaded, 1) != 0)
					return;
				// A domain that never started has nothing to end.
				Started?.ContinueWith (t => {
					try {
						Domain?.Unload ();
					} catch (Exception) {
					}
				}, TaskScheduler.Default);
			}
		}

		/// <summary>Receives the domain's unload request; bound to the generation that sent it.</summary>
		sealed class GenerationEvents : MarshalByRefObject, IApplicationDomainEvents
		{
			readonly WebFormsApplicationDomain owner;
			readonly Generation generation;

			public GenerationEvents (WebFormsApplicationDomain owner, Generation generation)
			{
				this.owner = owner;
				this.generation = generation;
			}

			public void UnloadRequested (string reason)
			{
				ThreadPool.QueueUserWorkItem (_ => owner.Retire (generation, "HttpRuntime.UnloadAppDomain (" + reason + ")"));
			}
		}
	}

	public static class WebFormsApplicationsExtensions
	{
		/// <summary>Serves each application from its own child domain, routed by virtual path.</summary>
		public static IApplicationBuilder UseWebFormsApplications (this IApplicationBuilder app, Action<WebFormsApplications> configure)
		{
			if (configure == null)
				throw new ArgumentNullException (nameof (configure));

			var applications = new WebFormsApplications ();
			configure (applications);
			return app.UseWebFormsApplications (applications);
		}

		/// <summary>Serves each application from its own child domain, routed by virtual path.</summary>
		public static IApplicationBuilder UseWebFormsApplications (this IApplicationBuilder app, WebFormsApplications applications)
		{
			if (app == null)
				throw new ArgumentNullException (nameof (app));
			if (applications == null)
				throw new ArgumentNullException (nameof (applications));

			ILogger logger = app.ApplicationServices.GetService<ILoggerFactory> ()?.CreateLogger ("System.Web.Hosting.Kestrel.WebFormsApplications");
			foreach (WebFormsApplicationDomain domain in applications.Domains) {
				domain.Logger = logger;
				if (domain.Options.PreloadEnabled)
					_ = domain.StartAsync ();
			}

			// Ending the processes gracefully is what lets each application stop its registered objects.
			app.ApplicationServices.GetService<IHostApplicationLifetime> ()?.ApplicationStopping.Register (applications.Dispose);

			return app.Use (next => context => {
				WebFormsApplicationDomain domain = applications.Find (context.Request.Path.Value ?? "/");
				return domain == null ? next (context) : domain.ProcessAsync (context);
			});
		}
	}
}
