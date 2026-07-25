<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Session.aspx.cs" Inherits="SessionStateSample.SessionPage" %>
<!DOCTYPE html>
<html>
<head runat="server"><title>Session state</title></head>
<body>
    <form id="form1" runat="server">
        <h1>Session state</h1>
        <!-- Rendered as plain labelled lines so an HTTP test can assert on them without parsing
             markup. The interesting behaviour is entirely server-side. -->
        <p>mode: <asp:Literal ID="Mode" runat="server" /></p>
        <p>id: <asp:Literal ID="Id" runat="server" /></p>
        <p>result: <asp:Literal ID="Result" runat="server" /></p>
        <p>hits: <asp:Literal ID="Hits" runat="server" /></p>
    </form>
</body>
</html>
