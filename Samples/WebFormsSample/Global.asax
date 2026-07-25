<%@ Application Language="C#" %>
<script runat="server">

    // global.asax is compiled by BuildManager into an HttpApplication subclass, and these methods are
    // discovered and wired by name (HttpApplicationFactory), not by an interface.

    void Application_Start (object sender, EventArgs e)
    {
        Application ["startedAt"] = DateTime.UtcNow.ToString ("O");
        Application ["requests"] = 0;
    }

    void Application_BeginRequest (object sender, EventArgs e)
    {
        Application.Lock ();
        try {
            Application ["requests"] = ((int) Application ["requests"]) + 1;
        } finally {
            Application.UnLock ();
        }
        // Proves a global.asax handler can touch the response for every request.
        Response.AppendHeader ("X-Global-Asax", "begin-request");
    }

    void Session_Start (object sender, EventArgs e)
    {
        Session ["visits"] = 0;
    }

</script>
