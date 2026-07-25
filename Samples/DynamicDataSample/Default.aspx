<%@ Page Language="C#" %>
<%@ Import Namespace="System.Web.DynamicData" %>
<%--
  The scaffolding entry point: every table the model registered, linked to its List page.

  MetaModel.Default is the model registered in Global.asax; GetTables () is what ScaffoldAllTables
  produced, and it is where the provider's conventions become visible - Products and Categories are
  here, and ConnectionString is not, because a string is IEnumerable<char> and the provider excludes it
  explicitly.
--%>
<!DOCTYPE html>
<html>
<head runat="server">
  <title>Dynamic Data sample</title>
</head>
<body>
  <form id="form1" runat="server">

    <h1>Tables</h1>

    <asp:Repeater ID="Tables" runat="server">
      <ItemTemplate>
        <div class="table-entry">
          <a href="<%# Eval ("ListActionPath") %>"><%# Eval ("DisplayName") %></a>
        </div>
      </ItemTemplate>
    </asp:Repeater>

    <p><a href="Products.aspx">LinqDataSource page</a></p>

  </form>

  <script runat="server">

    protected void Page_Load (object sender, EventArgs e)
    {
        Tables.DataSource = MetaModel.Default.VisibleTables;
        Tables.DataBind ();
    }

  </script>
</body>
</html>
