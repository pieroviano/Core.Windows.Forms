//
// Hosts the Web Pages application in this directory under Kestrel. The same UseWebForms call as every
// other sample - Web Pages is a module and a handler registered inside the ported System.Web runtime.
//

using Microsoft.AspNetCore.Builder;
using System.Web.Hosting.Kestrel;

var builder = WebApplication.CreateBuilder (args);
var app = builder.Build ();

app.UseStaticFiles ();

app.UseWebForms (options => {
	options.PhysicalPath = app.Environment.ContentRootPath;
	options.VirtualPath = "/";
	options.SiteName = "WebPagesSample";
});

app.Run ();
