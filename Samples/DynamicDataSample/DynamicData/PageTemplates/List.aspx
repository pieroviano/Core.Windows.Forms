<%@ Page Language="C#" %>
<%@ Import Namespace="System.Web.DynamicData" %>
<%@ Import Namespace="System.Linq" %>
<%--
  The List scaffolding template. DynamicDataRouteHandler resolves {table}/List.aspx to this file for
  EVERY table - the page never names Product or Category, it asks the route which table it is serving
  and reads the model.

  The columns are <asp:DynamicField>s built from MetaTable.Columns, which is what makes this
  scaffolding rather than a grid with a loop in it: each field resolves its own header, format, null
  text and editability from the model, and renders through the FieldTemplates/ control matching the
  column's type. Swapping DynamicData/FieldTemplates/Boolean.ascx changes every boolean in the
  application, on every table, without touching this file.
--%>
<!DOCTYPE html>
<html>
<head runat="server">
  <title>List</title>
</head>
<body>
  <form id="form1" runat="server">

    <h1 id="TableName" runat="server"></h1>

    <asp:GridView ID="Grid" runat="server" AutoGenerateColumns="false"
                  AutoGenerateEditButton="true"
                  OnRowEditing="Grid_RowEditing"
                  OnRowCancelingEdit="Grid_RowCancelingEdit"
                  OnRowUpdating="Grid_RowUpdating" />

    <p><a href="~/Default.aspx" runat="server">All tables</a></p>

  </form>

  <script runat="server">

    MetaTable table;

    protected void Page_Init (object sender, EventArgs e)
    {
        // Which table this request is for. The route handler put it here; nothing on the page said it.
        table = DynamicDataRouteHandler.GetRequestMetaTable (Context);

        foreach (MetaColumn column in table.Columns) {
            if (!column.Scaffold)
                continue;

            // DataField is all that is set. HeaderText comes from the column's DisplayName,
            // DataFormatString and NullDisplayText from its metadata, and the control that renders it
            // from FieldTemplates/ - which is the difference between this and a BoundField.
            Grid.Columns.Add (new DynamicField { DataField = column.Name });
        }

        // Only if there is one. A table with no discoverable primary key stays listable - the model
        // provider deliberately leaves it that way rather than failing - and an empty DataKeyNames
        // entry would make GridView look up a property named "".
        if (table.HasPrimaryKey)
            Grid.DataKeyNames = table.PrimaryKeyColumns.Select (c => c.Name).ToArray ();

        // How every DynamicField in this grid finds its column. Without it FindMetaTable walks up
        // looking for an IDynamicDataSource, finds none - the grid is bound to a plain IQueryable -
        // and each field throws from ResolveColumn.
        Grid.SetMetaTable (table);
    }

    protected void Page_Load (object sender, EventArgs e)
    {
        TableName.InnerText = table.DisplayName;

        if (!IsPostBack)
            Bind ();
    }

    void Bind ()
    {
        // GetQuery () builds the context through the factory registered in Global.asax and returns
        // the IQueryable for this table. That is the whole data layer: no DataContext anywhere.
        Grid.DataSource = table.GetQuery ();
        Grid.DataBind ();
    }

    protected void Grid_RowEditing (object sender, GridViewEditEventArgs e)
    {
        Grid.EditIndex = e.NewEditIndex;
        Bind ();
    }

    protected void Grid_RowCancelingEdit (object sender, GridViewCancelEditEventArgs e)
    {
        Grid.EditIndex = -1;
        Bind ();
    }

    protected void Grid_RowUpdating (object sender, GridViewUpdateEventArgs e)
    {
        // e.NewValues is filled by DynamicField.ExtractValuesFromCell, which walks down to each field
        // template and asks it for its own value. The page never reads a TextBox: it does not know
        // which controls the templates used, and that is the point.
        object row = FindRow (e.Keys);

        if (row != null)
            foreach (System.Collections.DictionaryEntry entry in e.NewValues) {
                var property = row.GetType ().GetProperty ((string) entry.Key);
                if (property != null && property.CanWrite)
                    property.SetValue (row, entry.Value);
            }

        Grid.EditIndex = -1;
        Bind ();
    }

    object FindRow (System.Collections.Specialized.IOrderedDictionary keys)
    {
        if (!table.HasPrimaryKey || keys.Count == 0)
            return null;

        MetaColumn key = table.PrimaryKeyColumns [0];
        object wanted = keys [key.Name];

        foreach (object candidate in table.GetQuery ())
            if (Equals (candidate.GetType ().GetProperty (key.Name).GetValue (candidate), wanted))
                return candidate;

        return null;
    }

  </script>
</body>
</html>
