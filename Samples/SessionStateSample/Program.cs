//
// Hosts the sample with out-of-process session state.
//
// Two lines that a ported application adds, and nothing else changes:
//
//   1. register an IDistributedCache - this is what mode="StateServer" actually stores into;
//   2. app.UseWebFormsSessionState (), BEFORE UseWebForms, to hand it to the provider model.
//
// AddDistributedMemoryCache is the single-instance choice and is used here so the sample runs with no
// infrastructure. Swap in AddStackExchangeRedisCache and the same web.config, the same pages and the
// same session code work across as many instances as you like - which is the whole reason this mode
// exists.
//

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Web.Hosting.Kestrel;
using System.Web.SessionState;

var builder = WebApplication.CreateBuilder (args);

builder.Services.AddDistributedMemoryCache ();

var app = builder.Build ();

app.UseWebFormsSessionState ();

app.UseWebForms (options => {
	options.PhysicalPath = app.Environment.ContentRootPath;
	options.VirtualPath = "/";
	options.SiteName = "SessionStateSample";
});

app.Run ();
