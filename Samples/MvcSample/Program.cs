//
// Hosts the MVC application in this directory under Kestrel. Identical to the WebForms samples' host -
// UseWebForms starts the ported System.Web runtime, and MVC is just an HttpModule and a handler
// registered inside it.
//

using Microsoft.AspNetCore.Builder;
using System.Web.Hosting.Kestrel;

var builder = WebApplication.CreateBuilder (args);
var app = builder.Build ();

app.UseStaticFiles ();

app.UseWebForms (options => {
	options.PhysicalPath = app.Environment.ContentRootPath;
	options.VirtualPath = "/";
	options.SiteName = "MvcSample";
	// Controllers live in this assembly and are found by name through MVC's controller factory, which
	// scans loaded assemblies - so it has to be loaded before the first request.
	options.ApplicationAssemblies = new [] { typeof (MvcSample.Controllers.HomeController).Assembly };
});

app.Run ();
