<%@ Application Language="C#" %>
<%@ Import Namespace="System.Web.Mvc" %>
<%@ Import Namespace="System.Web.Mvc.Routing" %>
<%@ Import Namespace="System.Web.Optimization" %>
<%@ Import Namespace="System.Web.Routing" %>
<script runat="server">

    // Routes are registered here, exactly as an ASP.NET MVC application does it. UrlRoutingModule
    // matches them during PostResolveRequestCache and calls HttpContext.RemapHandler to install
    // MvcHandler.
    void Application_Start (object sender, EventArgs e)
    {
        // Attribute routes FIRST. Routes match in order, and the conventional {controller}/{action}
        // route below matches very nearly everything - registered ahead of these it would swallow
        // every attribute route.
        RouteTable.Routes.MapMvcAttributeRoutes ();

        RouteTable.Routes.MapRoute (
            "Default",
            "{controller}/{action}/{id}",
            new { controller = "Home", action = "Index", id = UrlParameter.Optional });

        MvcSample.BundleConfig.RegisterBundles (BundleTable.Bundles);
    }

</script>
