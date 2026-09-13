<%@ Page Language="C#" Inherits="WebFormsSample.EventLogPage" AutoEventWireup="true" %>
<%--
  Every way a WebForms page raises a postback event.

    - Button.RaisePostBackEvent raises Click and THEN Command, and Command bubbles (RaiseBubbleEvent)
      up through every parent - here all the way to the page's OnBubbleEvent;
    - LinkButton and UseSubmitBehavior="false" post through __doPostBack rather than a submit;
    - ImageButton posts the click coordinates (name.x / name.y);
    - OnClientClick returning false cancels the postback in the browser;
    - HtmlButton, HtmlInputButton and HtmlAnchor raise ServerClick;
    - Panel.DefaultButton makes Enter inside the panel click that button, not the form's first submit;
    - PostBackUrl posts the form to ANOTHER page (cross-page posting), which reads it via PreviousPage.
--%>
<script runat="server">
    protected override bool OnBubbleEvent (object source, EventArgs args)
    {
        CommandEventArgs command = args as CommandEventArgs;
        if (command != null && command.CommandName.Length > 0)
            Log ("Page.BubbleEvent(" + ((Control) source).ID + "," + command.CommandName + ")");
        return false;
    }

    protected void PlainClick (object sender, EventArgs e) { Log ("plain.Click"); }
    protected void CmdClick (object sender, EventArgs e) { Log ("cmd.Click"); }
    protected void CmdCommand (object sender, CommandEventArgs e) { Log ("cmd.Command(" + e.CommandName + "," + e.CommandArgument + ")"); }
    protected void LinkClick (object sender, EventArgs e) { Log ("link.Click"); }
    protected void ImageClick (object sender, ImageClickEventArgs e) { Log ("img.Click(" + e.X + "," + e.Y + ")"); }
    protected void NoSubmitClick (object sender, EventArgs e) { Log ("noSubmit.Click"); }
    protected void CancelledClick (object sender, EventArgs e) { Log ("cancelled.Click"); }
    protected void HtmlButtonClick (object sender, EventArgs e) { Log ("htmlButton.ServerClick"); }
    protected void HtmlSubmitClick (object sender, EventArgs e) { Log ("htmlSubmit.ServerClick"); }
    protected void AnchorClick (object sender, EventArgs e) { Log ("anchor.ServerClick"); }
    protected void FirstClick (object sender, EventArgs e) { Log ("first.Click"); }
    protected void SecondClick (object sender, EventArgs e) { Log ("second.Click(" + entry.Text + ")"); }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>Buttons</title></head>
<body>
  <h1>Buttons</h1>
  <form id="form1" runat="server">
    <p>
      <asp:Button ID="plain" runat="server" Text="Button" OnClick="PlainClick" />
      <asp:Button ID="cmd" runat="server" Text="Command" CommandName="Add" CommandArgument="42"
                  OnClick="CmdClick" OnCommand="CmdCommand" />
      <asp:LinkButton ID="link" runat="server" Text="LinkButton" OnClick="LinkClick" />
      <asp:ImageButton ID="img" runat="server" AlternateText="ImageButton" Width="40" Height="40"
                       ImageUrl="data:image/gif;base64,R0lGODlhAQABAIAAAAAA/wAAACwAAAAAAQABAAACAkQBADs="
                       BorderWidth="0" OnClick="ImageClick" />
      <asp:Button ID="noSubmit" runat="server" Text="No submit" UseSubmitBehavior="false" OnClick="NoSubmitClick" />
      <asp:Button ID="cancelled" runat="server" Text="Cancelled" OnClientClick="return false;" OnClick="CancelledClick" />
    </p>
    <p>
      <button id="htmlButton" runat="server" type="submit" onserverclick="HtmlButtonClick">HtmlButton</button>
      <input id="htmlSubmit" runat="server" type="submit" value="HtmlInputButton" onserverclick="HtmlSubmitClick" />
      <a id="anchor" runat="server" onserverclick="AnchorClick">HtmlAnchor</a>
    </p>

    <asp:Panel ID="defaults" runat="server" DefaultButton="second">
      <asp:TextBox ID="entry" runat="server" />
      <asp:Button ID="first" runat="server" Text="First" OnClick="FirstClick" />
      <asp:Button ID="second" runat="server" Text="Second (default)" OnClick="SecondClick" />
    </asp:Panel>

    <p>
      <asp:TextBox ID="carry" runat="server" />
      <asp:Button ID="cross" runat="server" Text="Cross-page post" PostBackUrl="~/Events/CrossPageTarget.aspx" />
    </p>

    <h2>Events</h2>
    <p>Postbacks: <asp:Label ID="postbacks" runat="server" /></p>
    <p><asp:Label ID="log" runat="server" /></p>
  </form>
</body>
</html>
