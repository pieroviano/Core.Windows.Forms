using System;
using System.Web;

namespace WebFormsSample
{
	// Registered in web.config for the "hello.hello" path. This is the M2 gate: if it responds,
	// then AppDomain data, the configuration system, <httpHandlers> resolution, HttpApplication,
	// HttpRequest/HttpResponse and the Kestrel worker-request bridge are all working.
	public class HelloHandler : IHttpHandler
	{
		public bool IsReusable {
			get { return true; }
		}

		public void ProcessRequest (HttpContext context)
		{
			HttpResponse response = context.Response;
			response.ContentType = "text/plain";
			response.StatusCode = 200;
			response.AppendHeader ("X-WebForms-Port", "m2");
			response.Write ("Hello from the ported System.Web running under Kestrel.\n");
			response.Write ("Method:      " + context.Request.HttpMethod + "\n");
			response.Write ("Path:        " + context.Request.Path + "\n");
			response.Write ("RawUrl:      " + context.Request.RawUrl + "\n");
			response.Write ("Query q:     " + (context.Request.QueryString ["q"] ?? "<none>") + "\n");
			response.Write ("UserAgent:   " + (context.Request.UserAgent ?? "<none>") + "\n");
			response.Write ("PhysicalApp: " + context.Request.PhysicalApplicationPath + "\n");
			response.Write ("MapPath(~/): " + context.Server.MapPath ("~/") + "\n");
			// Read from inside an assembly compiled against the real
			// System.Configuration.ConfigurationManager package - exercises the facade. Caught here
			// rather than inside the probe: a type/member resolution failure happens on method
			// entry, before any try block inside Describe could run.
			string probe;
			try {
				probe = ThirdPartyProbe.Probe.Describe ();
			} catch (Exception e) {
				probe = "FAILED";
				for (Exception x = e; x != null; x = x.InnerException)
					probe += "\n             " + x.GetType ().FullName + ": " + x.Message;
			}
			response.Write ("ThirdParty:  " + probe + "\n");
		}
	}
}
