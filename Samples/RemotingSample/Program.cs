//
// Hosts the remoting sample.
//
// The *.rem endpoints need nothing here: root web.config maps them to HttpRemotingHandlerFactory, which
// applies this application's <system.runtime.remoting> section on the first request.
//
// mode="StateServer" needs one line, UseWebFormsRemoteStateServer, and a state server to talk to:
//
//     dotnet run --project Tools/state-server
//
// Pass --state-server to run one inside this process instead, for a single-command demo. It is the
// same RemoteStateServerHost the tool runs, so sessions then live and die with this process.
//

using System;
using System.Linq;
using System.Web.Hosting.Kestrel;
using System.Web.SessionState;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder (args);
var app = builder.Build ();

RemoteStateServerHost stateServer = args.Contains ("--state-server")
	? RemoteStateServerHost.Start ()
	: null;

app.UseWebFormsRemoteStateServer ();

app.UseWebForms (options => {
	options.PhysicalPath = app.Environment.ContentRootPath;
	options.VirtualPath = "/";
	options.SiteName = "RemotingSample";
});

app.Run ();
stateServer?.Dispose ();
