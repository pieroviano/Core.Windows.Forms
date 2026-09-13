<%@ Page Language="C#" %>
<%--
  The target of Buttons.aspx's PostBackUrl. The form was posted HERE, but it carries the source page's
  view state, so PreviousPage re-creates Buttons.aspx and runs it up to LoadComplete - its controls then
  hold the posted values. From this page's point of view the request is not a postback of itself.
--%>
<script runat="server">
    protected override void OnLoad (EventArgs e)
    {
        base.OnLoad (e);

        Page previous = PreviousPage;
        if (previous == null) {
            result.Text = "no previous page";
            return;
        }

        TextBox carried = (TextBox) previous.FindControl ("carry");
        result.Text = "previous=" + previous.AppRelativeVirtualPath
            + " carry=" + (carried == null ? "(not found)" : carried.Text)
            + " previous.IsCrossPagePostBack=" + previous.IsCrossPagePostBack
            + " IsPostBack=" + IsPostBack
            + " IsCrossPagePostBack=" + IsCrossPagePostBack;
    }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>Cross-page target</title></head>
<body>
  <h1>Cross-page target</h1>
  <p><asp:Label ID="result" runat="server" /></p>
</body>
</html>
