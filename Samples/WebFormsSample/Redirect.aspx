<%@ Page Language="C#" %>
<script runat="server">
    protected override void OnLoad (EventArgs e)
    {
        base.OnLoad (e);
        if (Request.QueryString ["to"] == "transfer") { Server.Transfer ("~/Simple.aspx"); return; }
        Response.Redirect ("~/Simple.aspx", false);
    }
</script>
