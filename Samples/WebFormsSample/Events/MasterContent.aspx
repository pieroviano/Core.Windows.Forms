<%@ Page Language="C#" MasterPageFile="~/Site.master" Inherits="WebFormsSample.EventLogPage" AutoEventWireup="true" %>
<%--
  Events on controls that live inside a master page's ContentPlaceHolder.

  The master page and the ContentPlaceHolder are naming containers, so the controls render with mangled
  names (ctl00$MainContent$...) and ids (MainContent_...). Postback data and the postback event have to
  be routed back through that hierarchy - Page.FindControl on the posted UniqueID - for the handlers
  below to run at all.
--%>
<script runat="server">
    protected void WhoChanged (object sender, EventArgs e) { Log ("who.TextChanged(" + who.Text + ")"); }
    protected void GoClick (object sender, EventArgs e) { Log ("go.Click"); }
    protected void PickChanged (object sender, EventArgs e) { Log ("pick.SelectedIndexChanged(" + pick.SelectedValue + ")"); }
</script>
<asp:Content ID="t" ContentPlaceHolderID="TitleContent" runat="server">Events in a content page</asp:Content>
<asp:Content ID="m" ContentPlaceHolderID="MainContent" runat="server">
  <form id="form1" runat="server">
    <h1>Events in a content page</h1>
    <p><asp:TextBox ID="who" runat="server" OnTextChanged="WhoChanged" /></p>
    <p><asp:DropDownList ID="pick" runat="server" AutoPostBack="true" OnSelectedIndexChanged="PickChanged">
        <asp:ListItem Value="one" /><asp:ListItem Value="two" /></asp:DropDownList></p>
    <p><asp:Button ID="go" runat="server" Text="Submit" OnClick="GoClick" /></p>
    <p>Postbacks: <asp:Label ID="postbacks" runat="server" /></p>
    <p><asp:Label ID="log" runat="server" /></p>
  </form>
</asp:Content>
