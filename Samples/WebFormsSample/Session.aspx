<%@ Page Language="C#" %>
<%@ Register TagPrefix="asp" Namespace="System.Web.UI.WebControls" Assembly="Core.Web" %>
<script runat="server">
    protected override void OnLoad (EventArgs e)
    {
        base.OnLoad (e);
        int visits = Session ["visits"] == null ? 0 : (int) Session ["visits"];
        visits++;
        Session ["visits"] = visits;
        info.Text = "visits=" + visits
                  + " sessionId=" + Session.SessionID
                  + " isNew=" + Session.IsNewSession
                  + " appRequests=" + Application ["requests"]
                  + " appStarted=" + (Application ["startedAt"] != null);
    }
</script>
<!DOCTYPE html>
<html><body><p><asp:Label ID="info" runat="server" /></p></body></html>
