<%@ Page Language="C#" Culture="de-DE" UICulture="de-DE" %>
<%@ Import Namespace="System.Globalization" %>
<%@ Import Namespace="System.Collections.Generic" %>
<script runat="server">
    protected override void OnLoad (EventArgs e)
    {
        base.OnLoad (e);
        // <%@ Import %> must have brought CultureInfo and List<T> into scope.
        culture.Text = CultureInfo.CurrentCulture.Name + " / " + CultureInfo.CurrentUICulture.Name;
        money.Text = (1234.5m).ToString ("N2");            // de-DE => 1.234,50
        when.Text = new DateTime (2026, 3, 4).ToString ("d");  // de-DE => 04.03.2026

        var rows = new List<Widget> {
            new Widget { Name = "alpha", Qty = 1 },
            new Widget { Name = "beta",  Qty = 2 },
            new Widget { Name = "gamma", Qty = 3 },
        };
        lv.DataSource = rows; lv.DataBind ();
        fv.DataSource = rows; fv.DataBind ();
        dv.DataSource = rows; dv.DataBind ();
    }

    public class Widget { public string Name { get; set; } public int Qty { get; set; } }
</script>
<!DOCTYPE html>
<html><head runat="server"><title>Locale</title></head><body>
  <form id="f" runat="server">
    <p>Culture: <asp:Label ID="culture" runat="server" /></p>
    <p>Number: <asp:Label ID="money" runat="server" /></p>
    <p>Date: <asp:Label ID="when" runat="server" /></p>
    <p>Local resource: <asp:Label ID="lbl" runat="server" meta:resourcekey="lbl" /></p>

    <h2>ListView</h2>
    <asp:ListView ID="lv" runat="server">
      <LayoutTemplate><ul runat="server" id="itemPlaceholder"></ul></LayoutTemplate>
      <ItemTemplate><li><%# Eval ("Name") %>=<%# Eval ("Qty") %></li></ItemTemplate>
    </asp:ListView>

    <h2>FormView</h2>
    <asp:FormView ID="fv" runat="server">
      <ItemTemplate>FormView: <%# Eval ("Name") %></ItemTemplate>
    </asp:FormView>

    <h2>DetailsView</h2>
    <asp:DetailsView ID="dv" runat="server" AutoGenerateRows="true" />
  </form>
</body></html>
