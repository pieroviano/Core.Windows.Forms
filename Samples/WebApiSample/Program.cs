//
// Hosts the Web API application in this directory under Kestrel.
//

using Microsoft.AspNetCore.Builder;
using System.Web.Hosting.Kestrel;

var builder = WebApplication.CreateBuilder (args);
var app = builder.Build ();

app.UseWebForms (options => {
	options.PhysicalPath = app.Environment.ContentRootPath;
	options.VirtualPath = "/";
	options.SiteName = "WebApiSample";
	options.ApplicationAssemblies = new [] { typeof (WebApiSample.Controllers.WidgetsController).Assembly };
});

app.Run ();
