<%@ Page Language="C#" %>
<script runat="server">
    protected void DoLogin (object sender, EventArgs e)
    {
        if (user.Text == "piero" && pass.Text == "secret") {
            // Issues the forms auth ticket cookie and redirects to the originally requested page.
            System.Web.Security.FormsAuthentication.RedirectFromLoginPage (user.Text, false);
            return;
        }
        msg.Text = "bad credentials";
    }
</script>
<!DOCTYPE html>
<html><body><form id="f" runat="server">
  <asp:TextBox ID="user" runat="server" />
  <asp:TextBox ID="pass" runat="server" TextMode="Password" />
  <asp:Button ID="go" runat="server" Text="Sign in" OnClick="DoLogin" />
  <asp:Label ID="msg" runat="server" />
</form></body></html>
