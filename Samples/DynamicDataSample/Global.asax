<%@ Application Language="C#" %>
<%@ Import Namespace="System.Web.DynamicData" %>
<%@ Import Namespace="System.Web.Routing" %>
<%@ Import Namespace="DynamicDataSample.Models" %>
<script runat="server">

    // Registration is unchanged from a .NET Framework Dynamic Data application. The only thing that
    // differs is what the context may be: QueryableDataModelProvider is the default provider here, so
    // ShopContext needs no attributes, no base class and no LINQ to SQL.
    void Application_Start (object sender, EventArgs e)
    {
        var model = new MetaModel ();
        model.RegisterContext (() => new ShopContext (),
                               new ContextConfiguration { ScaffoldAllTables = true });

        // {table}/{action}.aspx - the scaffolding convention. DynamicDataRouteHandler resolves each
        // match to DynamicData/PageTemplates/<action>.aspx, or to a CustomPages/<table>/ override.
        RouteTable.Routes.Add (new DynamicDataRoute ("{table}/{action}.aspx") {
            Constraints = new RouteValueDictionary (new { action = "List|Details|Edit|Insert" }),
            Model = model,
        });
    }

</script>
