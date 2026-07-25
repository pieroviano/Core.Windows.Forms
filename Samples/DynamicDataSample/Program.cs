//
// Hosts the Dynamic Data application in this directory under Kestrel. The same UseWebForms call as
// every other sample - Dynamic Data is a route handler and a set of controls registered inside the
// ported System.Web runtime, not a separate pipeline.
//

using Microsoft.AspNetCore.Builder;
using System.Web.Hosting.Kestrel;

var builder = WebApplication.CreateBuilder (args);
var app = builder.Build ();

app.UseStaticFiles ();

app.UseWebForms (options => {
	options.PhysicalPath = app.Environment.ContentRootPath;
	options.VirtualPath = "/";
	options.SiteName = "DynamicDataSample";
	// ShopContext is resolved by NAME from ContextTypeName in markup and from RegisterContext in
	// Global.asax, both through BuildManager, so this assembly has to be loaded before the first
	// request rather than whenever something happens to touch it.
	options.ApplicationAssemblies = new [] { typeof (DynamicDataSample.Models.ShopContext).Assembly };
});

app.Run ();
