<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Session.aspx.cs" Inherits="RemotingSample.SessionPage" %>
<!DOCTYPE html>
<html>
<head runat="server"><title>Remote session state</title></head>
<body>
    <form id="form1" runat="server">
        <h1>Remote session state</h1>
        <!-- Rendered as plain labelled lines so an HTTP test can assert on them without parsing
             markup. The interesting behaviour is entirely server-side. -->
        <p>mode: <asp:Literal ID="Mode" runat="server" /></p>
        <p>id: <asp:Literal ID="Id" runat="server" /></p>
        <p>result: <asp:Literal ID="Result" runat="server" /></p>
        <p>hits: <asp:Literal ID="Hits" runat="server" /></p>
        <p>pid: <asp:Literal ID="Pid" runat="server" /></p>
    </form>
</body>
</html>
