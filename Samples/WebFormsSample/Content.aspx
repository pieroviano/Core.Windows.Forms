<%@ Page Language="C#" MasterPageFile="~/Site.master" %>
<%@ Register TagPrefix="demo" TagName="WidgetBox" Src="~/WidgetBox.ascx" %>
<%@ Register TagPrefix="asp" Namespace="System.Web.UI.WebControls" Assembly="Core.Web" %>
<asp:Content ID="t" ContentPlaceHolderID="TitleContent" runat="server">Content page</asp:Content>
<asp:Content ID="m" ContentPlaceHolderID="MainContent" runat="server">
  <h1>Content from the child page</h1>
  <demo:WidgetBox runat="server" ID="w1" Caption="first" Count="3" />
  <demo:WidgetBox runat="server" ID="w2" Caption="second" Count="7" />
  <p>App_Code helper: <%= WebFormsSample.AppCode.Helper.Shout ("app_code works") %></p>
</asp:Content>
