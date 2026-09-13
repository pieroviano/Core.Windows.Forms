<%@ Page Language="C#" Inherits="WebFormsSample.EventLogPage" AutoEventWireup="true" %>
<%--
  Controls created in code rather than markup.

  A control added during Init takes part in the whole lifecycle like a declared one: its view state is
  restored and its posted data is loaded before Load. A control added as late as Load "catches up" -
  Control.AddedControl runs Init, view-state loading and Load for it on the spot - and its posted data
  is picked up by the SECOND ProcessPostData pass, which runs after LoadRecursive precisely so that
  controls created in Load can still raise their events. Both must be recreated, with the same IDs, on
  every request; the page only remembers state, never the controls themselves.
--%>
<script runat="server">
    Label dynLabel;
    TextBox dynBox;

    void Page_Init (object sender, EventArgs e)
    {
        dynLabel = new Label { ID = "dynLabel" };
        early.Controls.Add (dynLabel);

        dynBox = new TextBox { ID = "dynBox" };
        dynBox.TextChanged += (s, a) => Log ("dynBox.TextChanged(" + dynBox.Text + ")");
        early.Controls.Add (dynBox);

        Button dynButton = new Button { ID = "dynButton", Text = "Created in Init" };
        dynButton.Click += (s, a) => Log ("dynButton.Click");
        early.Controls.Add (dynButton);
    }

    void Page_Load (object sender, EventArgs e)
    {
        // Set once, on the first request only. It survives postbacks through the dynamic label's own
        // view state - which only works because the label is re-added at the same place in Init.
        if (!IsPostBack)
            dynLabel.Text = "label text from the first request";

        Button lateButton = new Button { ID = "lateButton", Text = "Created in Load" };
        lateButton.Click += (s, a) => Log ("lateButton.Click");
        late.Controls.Add (lateButton);
    }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>Dynamic controls</title></head>
<body>
  <h1>Dynamic controls</h1>
  <form id="form1" runat="server">
    <p>Added in Init: <asp:PlaceHolder ID="early" runat="server" /></p>
    <p>Added in Load: <asp:PlaceHolder ID="late" runat="server" /></p>

    <h2>Events</h2>
    <p>Postbacks: <asp:Label ID="postbacks" runat="server" /></p>
    <p><asp:Label ID="log" runat="server" /></p>
  </form>
</body>
</html>
