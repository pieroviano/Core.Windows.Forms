//
// Session through query-string operations, so an HTTP test can drive the store without a browser. The
// page renders the process id too: under UseWebFormsApplications it shows which child domain served it.
//

using System;
using System.Web.UI;

namespace RemotingSample
{
	public partial class SessionPage : Page
	{
		protected void Page_Load (object sender, EventArgs e)
		{
			int hits = Session ["hits"] is int ? (int) Session ["hits"] : 0;
			Session ["hits"] = hits + 1;

			Mode.Text = Session.Mode.ToString ();
			Id.Text = Session.SessionID;
			Hits.Text = Session ["hits"].ToString ();
			Pid.Text = Environment.ProcessId.ToString ();

			string key = Request.QueryString ["key"] ?? "value";
			switch (Request.QueryString ["op"]) {
			case "set":
				Session [key] = Request.QueryString ["value"];
				Result.Text = "set " + key;
				break;
			case "get":
				Result.Text = Session [key] as string ?? "(null)";
				break;
			default:
				Result.Text = "none";
				break;
			}
		}
	}
}
