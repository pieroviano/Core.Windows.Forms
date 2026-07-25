<%@ Page Language="C#" %>
<%@ Import Namespace="System.Web.Services" %>
<%@ Import Namespace="System.Web.Script.Services" %>

<script runat="server">
	// A "page method" is a static [WebMethod] on the page itself. ScriptManager emits a
	// PageMethods.* client proxy for these, and ScriptModule intercepts the JSON POST to
	// THIS page's own URL before the page ever runs its lifecycle.
	[WebMethod]
	public static string Echo (string text)
	{
		return "page method echo: " + text;
	}

	[WebMethod]
	public static int Multiply (int a, int b)
	{
		return a * b;
	}

	[WebMethod (EnableSession = true)]
	public static int Visit ()
	{
		var session = HttpContext.Current.Session;
		int n = session ["pmCount"] == null ? 0 : (int) session ["pmCount"];
		session ["pmCount"] = ++n;
		return n;
	}

	// Only static methods are page methods; this instance one must NOT be exposed.
	[WebMethod]
	public string NotAPageMethod ()
	{
		return "should not be reachable";
	}

	protected void Page_Load (object sender, EventArgs e)
	{
		lifecycle.Text = "page lifecycle ran";
	}
</script>

<html>
<head runat="server"><title>Page methods</title></head>
<body>
	<form id="form1" runat="server">
		<asp:ScriptManager ID="sm" runat="server" EnablePageMethods="true" />
		<asp:Label ID="lifecycle" runat="server" />
	</form>
</body>
</html>
