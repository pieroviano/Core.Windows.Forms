//
// Hosts a .svc endpoint alongside the ported System.Web.
//
// Two calls rather than one option on UseWebForms: CoreWCF is configured through dependency
// injection, so AddSvcEndpoints has to run while the service collection is still open - before
// builder.Build (). UseSvcEndpoints then inserts the middleware, and must come BEFORE UseWebForms,
// or System.Web's handler mapping takes the request first and has no idea what a .svc file is.
//

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Web.Hosting.Kestrel;
using System.Web.ServiceModel;

var builder = WebApplication.CreateBuilder (args);

builder.Services.AddSvcEndpoints (builder.Environment.ContentRootPath, options => {
	// The service types live in this assembly, which is the host's own - named explicitly so the
	// scan does not depend on something else having loaded it first.
	options.ServiceAssemblies = new [] { typeof (WcfSample.Services.EchoService).Assembly };
});

var app = builder.Build ();

app.UseSvcEndpoints ();

app.UseWebForms (options => {
	options.PhysicalPath = app.Environment.ContentRootPath;
	options.VirtualPath = "/";
	options.SiteName = "WcfSample";
});

app.Run ();
