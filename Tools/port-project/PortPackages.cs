//
// The port's NuGet package ids, in one place.
//
// They are not derivable from anything the tool can see: the directories are AspNetCore.*, the
// assemblies Core.*, and the ids a third scheme (AspNetCore.*) with exceptions where a plain name would
// mislead - Core.Web ships as ".Web.Forms", Core.Web.Http as ".Web.Http.Library", Core.Web.WebPages as
// ".WebPages.Base". Every id the tool emits or compares against comes from here, so the next rename is
// one file; GeneratedProjectBuildsTests restores each of them from the feed and fails on an id no
// project produces.
//

namespace PortProject
{
	static class PortPackages
	{
		/// <summary>The version a converted project references unless --package-version says otherwise.</summary>
		/// <remarks>
		/// Floating: the port packs 1.0.0.&lt;yyDDD&gt; on every build (Directory.Build.props), so an exact
		/// version would pin whichever daily build the tool happened to be compiled with.
		/// </remarks>
		public const string DefaultVersion = "1.0.0.*";

		public const string HostingKestrel = "Core.AspNet.Web.Hosting.Kestrel";
		public const string Mvc = "Core.AspNet.Web.Mvc";
		public const string Optimization = "Core.AspNet.Web.Optimization";
		public const string Http = "Core.AspNet.Web.Http.Library";
		public const string HttpWebHost = "Core.AspNet.Web.Http.WebHost";
		public const string HttpFormatting = "Core.AspNet.Net.Http.Formatting";
		public const string DynamicData = "Core.AspNet.Web.DynamicData";
		public const string ServiceModel = "Core.AspNet.Web.ServiceModel";
		public const string SessionState = "Core.AspNet.Web.SessionState";
		public const string WebPages = "Core.AspNet.Web.WebPages.Base";
		public const string WebPagesRazor = "Core.AspNet.Web.WebPages.Razor";
		public const string WebPagesDeployment = "Core.AspNet.Web.WebPages.Deployment";
		public const string Razor = "Core.AspNet.Web.Razor";
		public const string Infrastructure = "Core.AspNet.Web.Infrastructure";
	}
}
