<%@ Control Language="C#" ClassName="Tracer" %>
<%--
  A user control that records its own lifecycle into the page's trace, with a child control that does
  the same. Page_Init / Page_Load / Page_PreRender are wired by name (AutoEventWireup), exactly as they
  are on a page - a user control supports the same automatic handler names.
--%>
<script runat="server">
    void Note (string entry)
    {
        ((WebFormsSample.EventLogPage) Page).Note (entry);
    }

    void Page_Init (object sender, EventArgs e)    { Note ("uc.Init"); }
    void Page_Load (object sender, EventArgs e)    { Note ("uc.Load"); }
    void Page_PreRender (object sender, EventArgs e) { Note ("uc.PreRender"); }

    protected void InnerInit (object sender, EventArgs e)      { Note ("uc.inner.Init"); }
    protected void InnerLoad (object sender, EventArgs e)      { Note ("uc.inner.Load"); }
    protected void InnerPreRender (object sender, EventArgs e) { Note ("uc.inner.PreRender"); }
</script>
<span class="tracer"><asp:Label ID="inner" runat="server" Text="inside the user control"
    OnInit="InnerInit" OnLoad="InnerLoad" OnPreRender="InnerPreRender" /></span>
