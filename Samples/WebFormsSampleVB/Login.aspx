<%@ Page Language="VB" %>
<script runat="server">
    Protected Sub DoLogin(sender As Object, e As EventArgs)
        If user.Text = "piero" AndAlso pass.Text = "secret" Then
            System.Web.Security.FormsAuthentication.RedirectFromLoginPage(user.Text, False)
            Return
        End If
        msg.Text = "bad credentials"
    End Sub
</script>
<!DOCTYPE html>
<html><body><form id="f" runat="server">
  <asp:TextBox ID="user" runat="server" />
  <asp:TextBox ID="pass" runat="server" TextMode="Password" />
  <asp:Button ID="go" runat="server" Text="Sign in" OnClick="DoLogin" />
  <asp:Label ID="msg" runat="server" />
</form></body></html>
