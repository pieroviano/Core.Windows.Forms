<%@ Application Language="VB" %>
<script runat="server">
    Sub Application_Start(sender As Object, e As EventArgs)
        Application("startedAt") = DateTime.UtcNow.ToString("O")
    End Sub

    Sub Application_BeginRequest(sender As Object, e As EventArgs)
        Response.AppendHeader("X-Global-Asax-VB", "begin-request")
    End Sub
</script>
