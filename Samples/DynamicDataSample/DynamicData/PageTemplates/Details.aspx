<%@ Page Language="C#" %>
<%@ Import Namespace="System.Web.DynamicData" %>
<%@ Import Namespace="System.Linq" %>
<%--
  The Details scaffolding template - one row, selected by the primary key in the route.

  Worth noticing what is NOT here: no knowledge of which table, which key column, or what type the key
  is. MetaTable.PrimaryKeyColumns and the route values supply all three, which is the whole argument
  for scaffolding.
--%>
<!DOCTYPE html>
<html>
<head runat="server">
  <title>Details</title>
</head>
<body>
  <form id="form1" runat="server">

    <h1 id="TableName" runat="server"></h1>

    <asp:DetailsView ID="Details" runat="server" AutoGenerateRows="true" />

    <p id="NotFound" runat="server" visible="false">No such row.</p>

    <p><a href="~/Default.aspx" runat="server">All tables</a></p>

  </form>

  <script runat="server">

    protected void Page_Load (object sender, EventArgs e)
    {
        MetaTable table = DynamicDataRouteHandler.GetRequestMetaTable (Context);
        TableName.InnerText = table.DisplayName;

        object row = FindRow (table);

        if (row == null) {
            NotFound.Visible = true;
            Details.Visible = false;
            return;
        }

        Details.DataSource = new [] { row };
        Details.DataBind ();
    }

    // The key arrives as a route value named after the primary key column, converted from its string
    // form by the column's own type. A table with no primary key is listable but not addressable, and
    // reaches this page with nothing to look up.
    object FindRow (MetaTable table)
    {
        if (!table.HasPrimaryKey)
            return null;

        MetaColumn key = table.PrimaryKeyColumns [0];
        string raw = Page.RouteData.Values [key.Name] as string ?? Request.QueryString [key.Name];

        if (String.IsNullOrEmpty (raw))
            return null;

        object wanted;
        try {
            wanted = Convert.ChangeType (raw, key.ColumnType);
        } catch (FormatException) {
            return null;
        }

        foreach (object candidate in table.GetQuery ()) {
            object actual = candidate.GetType ().GetProperty (key.Name).GetValue (candidate);
            if (Equals (actual, wanted))
                return candidate;
        }

        return null;
    }

  </script>
</body>
</html>
