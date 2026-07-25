<%@ Page Language="VB" %>
<%@ Import Namespace="System.Globalization" %>
<script runat="server">
    ' A page whose whole code lives in the .aspx - the generated class is VB source produced by
    ' VBCodeProvider and compiled by the Roslyn VB backend.
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)

        ' Option Strict is Off for WebForms (ASP.NET's default), so this late-bound style compiles -
        ' which is exactly what a lot of real VB WebForms code relies on.
        Dim value As Object = "123"
        Dim asNumber As Integer = value

        info.Text = String.Format(CultureInfo.InvariantCulture,
                                  "late-bound conversion gave {0}; appSetting={1}; method={2}",
                                  asNumber + 1,
                                  ConfigurationManager.AppSettings("site.name"),
                                  Request.HttpMethod)
    End Sub
</script>
<!DOCTYPE html>
<html><body>
  <h1>Inline VB</h1>
  <p><asp:Label ID="info" runat="server" /></p>
  <%
     For i As Integer = 1 To 3
         Response.Write("<span>item " & i & "</span> ")
     Next
  %>
</body></html>
