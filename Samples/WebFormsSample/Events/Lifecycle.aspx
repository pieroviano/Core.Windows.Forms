<%@ Page Language="C#" Inherits="WebFormsSample.EventLogPage" AutoEventWireup="true" %>
<%@ Register TagPrefix="demo" TagName="Tracer" Src="~/Events/Tracer.ascx" %>
<%--
  The page lifecycle, as one request sees it.

  Every stage the page exposes is wired by name, plus Init/Load/PreRender on a TextBox, a user control
  and a control inside that user control. The order is fixed by Page.ProcessRequestMain and
  Control.InitRecursive / LoadRecursive / PreRenderRecursiveInternal:

    - Init runs children FIRST (depth-first), the page last;
    - Load and PreRender run the parent first, then children in tree order;
    - on a postback, changed events (RaiseChangedEvents) and then the postback event run between the
      controls' Load and the page's LoadComplete.
--%>
<script runat="server">
    void Page_PreInit (object sender, EventArgs e)          { Note ("Page.PreInit"); }
    void Page_Init (object sender, EventArgs e)             { Note ("Page.Init"); }
    void Page_InitComplete (object sender, EventArgs e)     { Note ("Page.InitComplete"); }
    void Page_PreLoad (object sender, EventArgs e)          { Note ("Page.PreLoad"); }
    void Page_Load (object sender, EventArgs e)             { Note ("Page.Load(IsPostBack=" + IsPostBack + ")"); }
    void Page_LoadComplete (object sender, EventArgs e)     { Note ("Page.LoadComplete"); }
    void Page_PreRender (object sender, EventArgs e)        { Note ("Page.PreRender"); }
    void Page_PreRenderComplete (object sender, EventArgs e) { Note ("Page.PreRenderComplete"); }
    void Page_SaveStateComplete (object sender, EventArgs e) { Note ("Page.SaveStateComplete"); }

    protected void BoxInit (object sender, EventArgs e)      { Note ("box.Init"); }
    protected void BoxLoad (object sender, EventArgs e)      { Note ("box.Load(Text=" + box.Text + ")"); }
    protected void BoxPreRender (object sender, EventArgs e) { Note ("box.PreRender"); }
    protected void BoxChanged (object sender, EventArgs e)   { Note ("box.TextChanged"); }
    protected void GoClick (object sender, EventArgs e)      { Note ("go.Click"); }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>Page lifecycle</title></head>
<body>
  <h1>Page lifecycle</h1>
  <form id="form1" runat="server">
    <p>
      <asp:TextBox ID="box" runat="server" OnInit="BoxInit" OnLoad="BoxLoad" OnPreRender="BoxPreRender"
                   OnTextChanged="BoxChanged" />
    </p>
    <p><demo:Tracer ID="uc" runat="server" /></p>
    <p><asp:Button ID="go" runat="server" Text="Post back" OnClick="GoClick" /></p>
    <h2>Trace of this request</h2>
    <p><asp:Label ID="trace" runat="server" /></p>
  </form>
</body>
</html>
