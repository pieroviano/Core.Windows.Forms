<%@ Page Language="C#" %>
<script runat="server">
    protected override void OnLoad (EventArgs e)
    {
        base.OnLoad (e);
        who.Text = "authenticated=" + User.Identity.IsAuthenticated + " name=" + User.Identity.Name;
    }
</script>
<!DOCTYPE html>
<html><body><h1>Secret</h1><p><asp:Label ID="who" runat="server" /></p></body></html>
