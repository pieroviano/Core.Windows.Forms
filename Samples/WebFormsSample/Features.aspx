<%@ Page Language="C#" Theme="Basic" Culture="en-US" UICulture="en-US" %>
<%@ OutputCache Duration="3600" VaryByParam="none" %>
<script runat="server">
    protected override void OnLoad (EventArgs e)
    {
        base.OnLoad (e);
        stamp.Text = "rendered at tick " + DateTime.UtcNow.Ticks;
        res.Text = Resources.Strings.Greeting;
        themed.Text = "themed label (skin should make me green+bold)";

        list.DataSource = new [] {
            new { Name = "alpha", Qty = 1 },
            new { Name = "beta",  Qty = 2 },
        };
        list.DataBind ();
    }
</script>
<!DOCTYPE html>
<html><head runat="server"><title>Features</title></head><body>
  <form id="f" runat="server">
    <p>Output cache stamp: <asp:Label ID="stamp" runat="server" /></p>
    <p>Global resource: <asp:Label ID="res" runat="server" /></p>
    <p><asp:Label ID="themed" runat="server" /></p>
    <h2>DataList</h2>
    <asp:DataList ID="list" runat="server">
      <ItemTemplate><%# Eval ("Name") %> x <%# Eval ("Qty") %></ItemTemplate>
    </asp:DataList>
    <h2>SiteMap navigation</h2>
    <asp:SiteMapDataSource ID="smds" runat="server" />
    <asp:Menu ID="menu" runat="server" DataSourceID="smds" />
    <asp:TreeView ID="tree" runat="server" DataSourceID="smds" />
  </form>
</body></html>
