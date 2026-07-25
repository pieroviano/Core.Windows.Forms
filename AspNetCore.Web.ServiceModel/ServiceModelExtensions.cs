//
// Public entry point: AddSvcEndpoints / UseSvcEndpoints.
//
// Why this is two calls rather than one option on UseWebForms
// -----------------------------------------------------------
// CoreWCF is configured through dependency injection: AddServiceModelServices () has to run while the
// service collection is still open, which is BEFORE builder.Build (). UseWebForms runs after, on the
// built application. There is no way to fold the registration into it, so the .svc scan happens at
// AddSvcEndpoints time and the middleware is inserted at UseSvcEndpoints time.
//
// Ordering: UseSvcEndpoints must come BEFORE UseWebForms. System.Web's own handler mapping would
// otherwise take the request first, and it has no idea what a .svc file is - it would serve the
// directive as text or refuse it outright.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoreWCF;
using CoreWCF.Channels;
using CoreWCF.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace System.Web.ServiceModel
{
	public sealed class SvcEndpointOptions
	{
		/// <summary>
		/// Application directory to scan for <c>.svc</c> files. Defaults to the host's content root.
		/// </summary>
		public string PhysicalPath { get; set; }

		/// <summary>
		/// Assemblies searched for the types named by <c>Service=</c>. The entry assembly and every
		/// already-loaded assembly are searched anyway; name yours here when it is neither.
		/// </summary>
		public IEnumerable<Assembly> ServiceAssemblies { get; set; }

		/// <summary>
		/// Binding used for every discovered endpoint. Defaults to <see cref="BasicHttpBinding"/>,
		/// which is SOAP 1.1 over HTTP - what an .svc served over http:// almost always was.
		/// </summary>
		public Binding Binding { get; set; }

		/// <summary>
		/// Whether <c>?wsdl</c> serves a metadata document. On by default: a .svc endpoint has always
		/// answered ?wsdl, and it is how clients generate proxies.
		/// </summary>
		public bool EnableWsdl { get; set; } = true;

		/// <summary>
		/// Whether a <c>.svc</c> whose service type cannot be resolved throws at startup. True by
		/// default: a service that silently fails to register looks exactly like one that is merely
		/// not being called yet, and the discovery only runs once so there is no later chance to
		/// notice.
		/// </summary>
		public bool ThrowOnUnresolvedService { get; set; } = true;
	}

	public static class ServiceModelExtensions
	{
		internal static readonly List<ResolvedService> Discovered = new List<ResolvedService> ();

		internal sealed class ResolvedService
		{
			public ServiceHostDirective Directive;
			public Type ServiceType;
			public Type ContractType;
		}

		/// <summary>
		/// Discovers the <c>.svc</c> files under the application and registers each with CoreWCF.
		/// Call on the builder's services, before <c>Build ()</c>.
		/// </summary>
		public static IServiceCollection AddSvcEndpoints (this IServiceCollection services,
								  string physicalPath,
								  Action<SvcEndpointOptions> configure = null)
		{
			if (services == null)
				throw new ArgumentNullException (nameof (services));

			var options = new SvcEndpointOptions { PhysicalPath = physicalPath };
			configure?.Invoke (options);

			if (String.IsNullOrEmpty (options.PhysicalPath))
				throw new ArgumentException ("An application physical path is required to find .svc files.",
							     nameof (physicalPath));

			// Loaded up front so Resolve can see them; a service type in an assembly nothing has
			// touched yet would otherwise be invisible.
			if (options.ServiceAssemblies != null) {
				foreach (Assembly assembly in options.ServiceAssemblies) {
					if (assembly != null)
						AppDomain.CurrentDomain.Load (assembly.GetName ());
				}
			}

			Discovered.Clear ();

			foreach (ServiceHostDirective directive in ServiceHostDirective.Discover (options.PhysicalPath)) {
				if (!String.IsNullOrEmpty (directive.FactoryTypeName))
					throw new NotSupportedException (
						$"{directive.VirtualPath} names Factory=\"{directive.FactoryTypeName}\". A custom " +
						"ServiceHostFactory has no equivalent here - CoreWCF builds the host itself. " +
						"Remove the attribute, or configure the service through CoreWCF directly.");

				Type serviceType = Resolve (directive.ServiceTypeName);
				if (serviceType == null) {
					if (options.ThrowOnUnresolvedService)
						throw new TypeLoadException (
							$"{directive.VirtualPath} names Service=\"{directive.ServiceTypeName}\", which " +
							"could not be resolved. Name the assembly in SvcEndpointOptions." +
							nameof (SvcEndpointOptions.ServiceAssemblies) + ", or qualify the type.");
					continue;
				}

				Type contractType = FindContract (serviceType);
				if (contractType == null) {
					if (options.ThrowOnUnresolvedService)
						throw new InvalidOperationException (
							$"{serviceType.FullName} has no [ServiceContract]. Put it on the class, or on " +
							"an interface the class implements.");
					continue;
				}

				Discovered.Add (new ResolvedService {
					Directive = directive,
					ServiceType = serviceType,
					ContractType = contractType,
				});
			}

			if (Discovered.Count == 0)
				return services;

			services.AddServiceModelServices ();
			services.AddSingleton (options);

			if (options.EnableWsdl)
				services.AddServiceModelMetadata ();

			return services;
		}

		/// <summary>
		/// Maps each discovered <c>.svc</c> onto its CoreWCF endpoint. Must be called BEFORE
		/// <c>UseWebForms ()</c>.
		/// </summary>
		public static IApplicationBuilder UseSvcEndpoints (this IApplicationBuilder app)
		{
			if (app == null)
				throw new ArgumentNullException (nameof (app));

			if (Discovered.Count == 0)
				return app;

			var options = app.ApplicationServices.GetService<SvcEndpointOptions> () ?? new SvcEndpointOptions ();
			Binding binding = options.Binding ?? new BasicHttpBinding ();

			var logger = app.ApplicationServices.GetService<ILoggerFactory> ()
				?.CreateLogger ("System.Web.ServiceModel");

			if (options.EnableWsdl) {
				// CoreWCF exposes the behaviour through DI rather than per-host description, so it is
				// switched on once here rather than added to each endpoint.
				var metadata = app.ApplicationServices
					.GetService<CoreWCF.Description.ServiceMetadataBehavior> ();
				if (metadata != null)
					metadata.HttpGetEnabled = true;
			}

			app.UseServiceModel (builder => {
				foreach (ResolvedService service in Discovered) {
					builder.AddService (service.ServiceType);
					builder.AddServiceEndpoint (service.ServiceType, service.ContractType, binding,
								    new Uri (service.Directive.VirtualPath, UriKind.Relative), null);

					logger?.LogInformation ("Hosting {Service} at {Path}",
								service.ServiceType.FullName, service.Directive.VirtualPath);
				}
			});

			return app;
		}

		/// <summary>
		/// Resolves the name in a <c>Service=</c> attribute, which may or may not be assembly-qualified.
		/// </summary>
		static Type Resolve (string typeName)
		{
			if (String.IsNullOrEmpty (typeName))
				return null;

			// Assembly-qualified, or resolvable from this assembly / corelib.
			Type type = Type.GetType (typeName, throwOnError: false);
			if (type != null)
				return type;

			// The usual case: a bare name, the way .svc files are almost always written. Scan what is
			// loaded, the same way HttpApplication.LoadType does for <httpHandlers>.
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies ()) {
				type = assembly.GetType (typeName, throwOnError: false);
				if (type != null)
					return type;
			}

			return null;
		}

		/// <summary>
		/// The contract to expose: the [ServiceContract] interface the class implements, or the class
		/// itself when the attribute is on the class.
		/// </summary>
		static Type FindContract (Type serviceType)
		{
			foreach (Type contract in serviceType.GetInterfaces ()) {
				if (contract.GetCustomAttribute<ServiceContractAttribute> () != null)
					return contract;
			}

			// WCF allows [ServiceContract] directly on the implementing class, with no interface.
			if (serviceType.GetCustomAttribute<ServiceContractAttribute> () != null)
				return serviceType;

			return null;
		}
	}
}
