//
// Hosts the WebForms application in this directory under Kestrel.
//

using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Web.Hosting.Kestrel;

var builder = WebApplication.CreateBuilder (args);
builder.Logging.SetMinimumLevel (LogLevel.Information);

var app = builder.Build ();

// Static files first: let Kestrel serve .css/.js/images directly rather than routing them through
// the System.Web pipeline.
app.UseStaticFiles ();

app.UseWebForms (options => {
	options.PhysicalPath = app.Environment.ContentRootPath;
	options.VirtualPath = "/";
	options.SiteName = "WebFormsSample";
	// Opt in to a serializer for state objects with no native encoding. Without this a custom type
	// in view state or session is refused with a diagnostic naming it, rather than silently
	// re-encoded - see IStateObjectSerializer.
	options.StateSerializer = new System.Web.JsonStateObjectSerializer ();
});

app.Run ();
