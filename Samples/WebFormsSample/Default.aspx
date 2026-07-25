<%@ Page Language="C#" Inherits="WebFormsSample.DefaultPage" %>
<%@ Register TagPrefix="asp" Namespace="System.Web.UI.WebControls" Assembly="Core.Web" %>
<!DOCTYPE html>
<html>
<head runat="server">
  <title>WebForms on Kestrel</title>
</head>
<body>
  <h1>WebForms on Kestrel</h1>

  <p>Server control set from code-behind: <asp:Label ID="message" runat="server" /></p>
  <p>Inline expression: <%= 6 * 7 %></p>
  <p>Data-bound literal: <asp:Literal ID="stamp" runat="server" /></p>

  <form id="form1" runat="server">
    <p>
      <asp:TextBox ID="who" runat="server" />
      <asp:RequiredFieldValidator ID="whoRequired" runat="server" ControlToValidate="who"
                                  ErrorMessage="a name is required" Display="Dynamic" />
      <asp:Button ID="greet" runat="server" Text="Greet" OnClick="OnGreet" />
    </p>
    <p><asp:Label ID="greeting" runat="server" /></p>
    <p>Postbacks so far: <asp:Label ID="counter" runat="server" /></p>

    <h2>GridView over a DataTable</h2>
    <asp:GridView ID="grid" runat="server" AutoGenerateColumns="true" />
  </form>

  <h2>Repeater over a list</h2>
  <asp:Repeater ID="items" runat="server">
    <HeaderTemplate><ul></HeaderTemplate>
    <ItemTemplate><li><%# Container.DataItem %></li></ItemTemplate>
    <FooterTemplate></ul></FooterTemplate>
  </asp:Repeater>
</body>
</html>
