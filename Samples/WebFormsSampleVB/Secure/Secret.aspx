<%@ Page Language="VB" %>
<script runat="server">
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        who.Text = "authenticated=" & User.Identity.IsAuthenticated & " name=" & User.Identity.Name
    End Sub
</script>
<!DOCTYPE html>
<html><body><h1>Secret (VB)</h1><p><asp:Label ID="who" runat="server" /></p></body></html>
