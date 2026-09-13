<%@ Page Language="C#" Inherits="WebFormsSample.EventLogPage" AutoEventWireup="true" %>
<%@ Register TagPrefix="asp" Namespace="System.Web.UI" Assembly="Core.Web.Extensions" %>
<%--
  Server events raised by asynchronous (partial) postbacks.

  An async postback runs the FULL page lifecycle on the server - every event below fires exactly as it
  would on a normal postback - but only the UpdatePanels that need refreshing are sent back and patched
  into the page by the client script. With UpdateMode="Conditional" that is: the panel whose child
  caused the postback, a panel named by an AsyncPostBackTrigger, or a panel whose Update () was called.

    - "stamp" is outside every panel: its new value is rendered on the server and then discarded;
    - a PostBackTrigger turns a control inside the panel back into a full postback.
--%>
<script runat="server">
    void Page_Load (object sender, EventArgs e)
    {
        stamp.Text = "request #" + (Postbacks + 1);
    }

    string Mode { get { return "async=" + sm.IsInAsyncPostBack; } }

    protected void InsideClick (object sender, EventArgs e) { Log ("inside.Click(" + Mode + ")"); }
    protected void ChoiceChanged (object sender, EventArgs e) { Log ("choice.SelectedIndexChanged(" + choice.SelectedValue + "," + Mode + ")"); }
    protected void OutsideClick (object sender, EventArgs e) { Log ("outside.Click(" + Mode + ")"); }
    protected void FullClick (object sender, EventArgs e) { Log ("full.Click(" + Mode + ")"); }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>Partial postback events</title></head>
<body>
  <h1>Partial postback events</h1>
  <form id="form1" runat="server">
    <asp:ScriptManager ID="sm" runat="server" EnablePartialRendering="true" />

    <p>Outside every panel: <asp:Label ID="stamp" runat="server" /></p>
    <p><asp:Button ID="outside" runat="server" Text="Outside trigger" OnClick="OutsideClick" /></p>

    <asp:UpdatePanel ID="up" runat="server" UpdateMode="Conditional">
      <ContentTemplate>
        <p>
          <asp:Button ID="inside" runat="server" Text="Inside the panel" OnClick="InsideClick" />
          <asp:DropDownList ID="choice" runat="server" AutoPostBack="true" OnSelectedIndexChanged="ChoiceChanged">
            <asp:ListItem Value="a" /><asp:ListItem Value="b" />
          </asp:DropDownList>
          <asp:Button ID="full" runat="server" Text="Full postback" OnClick="FullClick" />
        </p>
        <p>Postbacks: <asp:Label ID="postbacks" runat="server" /></p>
        <p><asp:Label ID="log" runat="server" /></p>
      </ContentTemplate>
      <Triggers>
        <asp:AsyncPostBackTrigger ControlID="outside" EventName="Click" />
        <asp:PostBackTrigger ControlID="full" />
      </Triggers>
    </asp:UpdatePanel>
  </form>
</body>
</html>
