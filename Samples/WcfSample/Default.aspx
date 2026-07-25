<%@ Page Language="C#" %>
<!DOCTYPE html>
<html>
<head runat="server"><title>WCF sample</title></head>
<body>
    <form id="form1" runat="server">
        <h1>WCF sample</h1>
        <!-- Here to prove the two coexist: .svc is served by CoreWCF, this page by the ported
             System.Web, in one application on one port. -->
        <p id="hosted" runat="server">Services: /Echo.svc, /Api/Calculator.svc</p>
        <asp:Label ID="Status" runat="server" Text="webforms alive" />
    </form>
</body>
</html>
