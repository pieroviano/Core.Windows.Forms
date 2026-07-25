<%@ Page Language="C#" Inherits="WebFormsSample.EventsPage" %>
<%@ Register TagPrefix="asp" Namespace="System.Web.UI.WebControls" Assembly="Core.Web" %>
<!DOCTYPE html>
<html>
<head runat="server">
  <title>Server-side events</title>
</head>
<body>
  <h1>Server-side events</h1>

  <p>
    Change the text, pick a different colour, then submit. All three server-side events are raised by
    the one postback, in lifecycle order: the changed events during RaiseChangedEvents, the click
    after them.
  </p>

  <form id="form1" runat="server">
    <p>
      <label for="who">Name</label>
      <asp:TextBox ID="who" runat="server" OnTextChanged="OnWhoTextChanged" />
    </p>
    <p>
      <label for="colour">Colour</label>
      <asp:DropDownList ID="colour" runat="server" OnSelectedIndexChanged="OnColourSelectedIndexChanged">
        <asp:ListItem Value="red" Text="Red" />
        <asp:ListItem Value="green" Text="Green" />
        <asp:ListItem Value="blue" Text="Blue" />
      </asp:DropDownList>
    </p>
    <p>
      <asp:Button ID="go" runat="server" Text="Submit" OnClick="OnGoClick" />
    </p>

    <h2>Events</h2>
    <p>Count: <asp:Label ID="count" runat="server" /></p>
    <p><asp:Label ID="log" runat="server" /></p>
  </form>
</body>
</html>
