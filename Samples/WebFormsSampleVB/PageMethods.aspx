<%@ Page Language="VB" %>
<%@ Import Namespace="System.Web.Services" %>

<script runat="server">
	' Page methods in VB: Shared, not static, and the attribute goes in angle brackets. Everything
	' downstream - ScriptModule's PathInfo dispatch, JavaScriptSerializer argument binding, the
	' {"d":...} response envelope - is language-agnostic, so this exercises the VB compilation path
	' rather than a separate feature.
	<WebMethod()> _
	Public Shared Function Echo(ByVal text As String) As String
		Return "vb page method echo: " & text
	End Function

	<WebMethod()> _
	Public Shared Function Multiply(ByVal a As Integer, ByVal b As Integer) As Integer
		Return a * b
	End Function

	' Option Strict is Off here, as it is for ASP.NET generally, so the late-bound arithmetic on a
	' String compiles - and the page method must return the Integer that produces.
	<WebMethod()> _
	Public Shared Function LateBound(ByVal text As String) As Integer
		Dim n = text
		Return n + 1
	End Function

	Protected Sub Page_Load(ByVal sender As Object, ByVal e As EventArgs)
		lifecycle.Text = "vb page lifecycle ran"
	End Sub
</script>

<html>
<head runat="server"><title>VB page methods</title></head>
<body>
	<form id="form1" runat="server">
		<asp:ScriptManager ID="sm" runat="server" EnablePageMethods="true" />
		<asp:Label ID="lifecycle" runat="server" />
	</form>
</body>
</html>
