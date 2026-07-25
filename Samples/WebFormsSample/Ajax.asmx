<%@ WebService Language="C#" Class="WebFormsSample.AjaxService" %>

using System;
using System.Collections.Generic;
using System.Web.Services;
using System.Web.Script.Services;

namespace WebFormsSample
{
	// [ScriptService] is what makes the .asmx answer JSON as well as SOAP, and what makes
	// Ajax.asmx/js emit a client proxy. Without it the service is SOAP-only.
	[WebService (Namespace = "http://webformsport/")]
	[ScriptService]
	public class AjaxService : WebService
	{
		[WebMethod]
		[ScriptMethod (ResponseFormat = ResponseFormat.Json)]
		public string Greet (string name)
		{
			return "hello " + name + " (from a script service)";
		}

		[WebMethod]
		public int AddUp (int a, int b)
		{
			return a + b;
		}

		// UseHttpGet proves the GET half of the JSON dispatcher, which takes a different path
		// through RestHandler than the POST one.
		[WebMethod]
		[ScriptMethod (UseHttpGet = true)]
		public string Now (string prefix)
		{
			return prefix + ":ok";
		}

		// A complex return value: exercises the JavaScriptSerializer round trip rather than just
		// string passthrough.
		[WebMethod]
		public Widget Describe (int id)
		{
			return new Widget { Id = id, Name = "widget-" + id, Tags = new List<string> { "a", "b" } };
		}

		// Session state only flows when the method asks for it - EnableSession is what makes
		// RestHandler wrap the handler in IRequiresSessionState.
		[WebMethod (EnableSession = true)]
		public int Bump ()
		{
			int n = Session ["ajaxCount"] == null ? 0 : (int) Session ["ajaxCount"];
			Session ["ajaxCount"] = ++n;
			return n;
		}

		[WebMethod]
		public string Boom ()
		{
			throw new InvalidOperationException ("deliberate failure");
		}
	}

	public class Widget
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public List<string> Tags { get; set; }
	}
}
