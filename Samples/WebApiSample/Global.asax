<%@ Application Language="C#" %>
<%@ Import Namespace="System.Web.Http" %>
<%@ Import Namespace="System.Web.Routing" %>
<script runat="server">

    // The standard Web API registration. MapHttpRoute is an extension on RouteCollection from
    // Core.Web.Http.WebHost; it installs an HttpControllerRouteHandler, which UrlRoutingModule then
    // remaps the request to.
    void Application_Start (object sender, EventArgs e)
    {
        GlobalConfiguration.Configuration.Routes.MapHttpRoute (
            name: "DefaultApi",
            routeTemplate: "api/{controller}/{id}",
            // Fully qualified: Global.asax imports System.Web.UI.WebControls by default, which has a
            // RouteParameter of its own, and the bare name is ambiguous between the two.
            defaults: new { id = System.Web.Http.RouteParameter.Optional });
    }

</script>
