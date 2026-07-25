<%@ Page Language="C#" %>
<%@ Register TagPrefix="asp" Namespace="System.Web.UI" Assembly="Core.Web.Extensions" %>
<%@ Register TagPrefix="asp" Namespace="System.Web.UI.WebControls" Assembly="Core.Web" %>
<script runat="server">
    protected override void OnLoad (EventArgs e)
    {
        base.OnLoad (e);
        // Stamped once per full page load; a partial postback must not refresh it.
        if (!IsPostBack)
            outside.Text = "rendered at tick " + DateTime.UtcNow.Ticks;
        else
            outside.Text = (string) (ViewState ["outside"] ?? "");
        ViewState ["outside"] = outside.Text;
    }

    protected void Bump (object sender, EventArgs e)
    {
        int n = ViewState ["n"] == null ? 0 : (int) ViewState ["n"];
        n++;
        ViewState ["n"] = n;
        inside.Text = "partial update #" + n + " at tick " + DateTime.UtcNow.Ticks;
    }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>UpdatePanel</title></head>
<body>
  <form id="f" runat="server">
    <asp:ScriptManager ID="sm" runat="server" EnablePartialRendering="true" />
    <p>Outside the panel (must NOT change on a partial postback): <asp:Label ID="outside" runat="server" /></p>
    <asp:UpdatePanel ID="up" runat="server" UpdateMode="Conditional">
      <ContentTemplate>
        <p>Inside the panel: <asp:Label ID="inside" runat="server" Text="not updated yet" /></p>
        <asp:Button ID="go" runat="server" Text="Update panel only" OnClick="Bump" />
      </ContentTemplate>
    </asp:UpdatePanel>
  </form>
</body>
</html>
