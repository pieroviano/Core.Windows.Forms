//
// DynamicDataRouteHandler.cs
//
// Authors:
//	Atsushi Enomoto <atsushi@ximian.com>
//      Marek Habersack <mhabersack@novell.com>
//
// Copyright (C) 2008-2009 Novell Inc. http://novell.com
//

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// PORT NOTE - why this is an override rather than a patch
// -------------------------------------------------------
// Two changes, one cosmetic and one that Dynamic Data does not work without.
//
// 1. `using System.Data.Linq.Mapping` is dropped. LINQ to SQL is not on .NET, and this file never
//    used the namespace. That much was a line-preserving patch and could have stayed one.
//
// 2. The per-request RouteContext is now stored in HttpContext.Items, and GetHttpHandler actually
//    registers it. Upstream declares a static Dictionary<HttpContext, RouteContext> and a
//    ReaderWriterLockSlim guarding it, but *nothing on the request path ever adds to it* -
//    GetHttpHandler builds a RouteContext, uses it as a key into its own handler cache, and throws it
//    away. So GetRequestMetaTable returned null on every real request, and every scaffolded page died
//    with a NullReferenceException on its first line. The file is marked
//    [MonoTODO ("Needs a working test")] and the class comment says the code "is a result of guessing
//    as no tests succeed for this call so far", which is exactly what an unexercised code path looks
//    like years later.
//
//    Items is also the right container regardless: the static dictionary is keyed by HttpContext and
//    has no removal anywhere in the file, so populating it as written would have leaked one entry -
//    and one whole request context - per request, for the life of the process. Items is torn down
//    with the request, needs no lock because it is not shared, and makes GetOrCreateRouteContext's
//    read/write lock dance unnecessary.
//
// Found by Samples/DynamicDataSample and Tests/AspNetCore.Web.LegacyStacks.HttpTests, which exist
// because unit tests over the model provider could not have caught this: the model was always fine.
//
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Security.Permissions;
using System.Security.Principal;
using System.Threading;
using System.Web.Caching;
using System.Web.Compilation;
using System.Web.Hosting;
using System.Web.Routing;
using System.Web.UI;

namespace System.Web.DynamicData
{
	[AspNetHostingPermission (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	[AspNetHostingPermission (SecurityAction.InheritanceDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	public class DynamicDataRouteHandler : IRouteHandler
	{
		// port: was a static Dictionary<HttpContext, RouteContext> plus a ReaderWriterLockSlim. See the
		// note at the top of the file - one entry per request, never removed.
		const string RouteContextKey = "System.Web.DynamicData.DynamicDataRouteHandler.RouteContext";

		Dictionary <RouteContext, IHttpHandler> handlers;

		Dictionary <RouteContext, IHttpHandler> Handlers {
			get {
				if (handlers == null)
					handlers = new Dictionary <RouteContext, IHttpHandler> ();

				return handlers;
			}
		}

		static RouteContext FindRouteContext (HttpContext httpContext)
		{
			return httpContext.Items [RouteContextKey] as RouteContext;
		}

		static RouteContext GetOrCreateRouteContext (HttpContext httpContext)
		{
			RouteContext rc = FindRouteContext (httpContext);
			if (rc != null)
				return rc;

			// The fallback upstream had: synthesise one from an empty RouteData. It yields a context
			// with no route, no action and no table, which is right for a page reached outside a
			// DynamicDataRoute - SetRequestMetaTable on such a page is how a custom page opts in.
			rc = MakeRouteContext (new RequestContext (new HttpContextWrapper (httpContext), new RouteData ()), null, null, null);
			httpContext.Items [RouteContextKey] = rc;
			return rc;
		}

		// port: what upstream never did. Called from GetHttpHandler with the context built from the
		// route that actually matched, so GetRequestMetaTable has something to return.
		static void SetRouteContext (HttpContext httpContext, RouteContext rc)
		{
			if (httpContext != null)
				httpContext.Items [RouteContextKey] = rc;
		}
		
		public static RequestContext GetRequestContext (HttpContext httpContext)
		{
			if (httpContext == null)
				throw new ArgumentNullException ("httpContext");
			
			return GetOrCreateRouteContext (httpContext).Context;
		}

		public static MetaTable GetRequestMetaTable (HttpContext httpContext)
		{
			if (httpContext == null)
				throw new ArgumentNullException ("httpContext");

			RouteContext rc = FindRouteContext (httpContext);
			return rc == null ? null : rc.Table;
		}

		public static void SetRequestMetaTable (HttpContext httpContext, MetaTable table)
		{
			// And tradiationally... some .NET emulation code
			if (httpContext == null)
				throw new NullReferenceException ();

			GetOrCreateRouteContext (httpContext).Table = table;
		}

		public DynamicDataRouteHandler ()
		{
		}

		public MetaModel Model { get; internal set; }

		[MonoTODO ("Needs a working test")]
		public virtual IHttpHandler CreateHandler (DynamicDataRoute route, MetaTable table, string action)
		{
			// .NET bug emulation mode
			if (route == null || table == null || action == null)
				throw new NullReferenceException ();

			// NOTE: all code below is a result of guessing as no tests succeed for this
			// call so far!

			IHttpHandler ret = null;
			
			// Give custom pages a chance
			string viewName = String.IsNullOrEmpty (action) ? route.ViewName : action;
			string path = GetCustomPageVirtualPath (table, viewName);

			// Pages might be in app resources, need to use a VPP
			VirtualPathProvider vpp = HostingEnvironment.VirtualPathProvider;
			
			if (vpp != null && vpp.FileExists (path))
				ret = BuildManager.CreateInstanceFromVirtualPath (path, typeof (Page)) as IHttpHandler;

			if (ret != null)
				return ret;

			path = GetScaffoldPageVirtualPath (table, viewName);
			if (vpp != null && vpp.FileExists (path))
				ret = BuildManager.CreateInstanceFromVirtualPath (path, typeof (Page)) as IHttpHandler;
			
			return ret;
		}

		protected virtual string GetCustomPageVirtualPath (MetaTable table, string viewName)
		{
			// No such checks are made in .NET, we won't follow the pattern...
			MetaModel model = Model;
			if (table == null || model == null)
				throw new NullReferenceException (); // yuck

			// Believe it or not, this is what .NET does - pass a null/empty viewName
			// and you get /.aspx at the end...
			return model.DynamicDataFolderVirtualPath + "CustomPages/" + table.Name + "/" + viewName + ".aspx";
		}

		protected virtual string GetScaffoldPageVirtualPath (MetaTable table, string viewName)
		{
			// No such checks are made in .NET, we won't follow the pattern...
			MetaModel model = Model;
			if (table == null || model == null)
				throw new NullReferenceException (); // yuck

			// Believe it or not, this is what .NET does - pass a null/empty viewName
			// and you get /.aspx at the end...
			return model.DynamicDataFolderVirtualPath + "PageTemplates/" + viewName + ".aspx";
		}

		IHttpHandler IRouteHandler.GetHttpHandler (RequestContext requestContext)
		{
			if (requestContext == null)
				throw new ArgumentNullException ("requestContext");
			RouteData rd = requestContext.RouteData;
			var dr = rd.Route as DynamicDataRoute;
			if (dr == null)
				throw new ArgumentException ("The argument RequestContext does not have DynamicDataRoute in its RouteData");
			string action = dr.GetActionFromRouteData (rd);
			MetaTable mt = dr.GetTableFromRouteData (rd);
			RouteContext rc = MakeRouteContext (requestContext, dr, action, mt);

			// port: publish it for GetRequestMetaTable / GetRequestContext. This is the whole fix; see
			// the note at the top. HttpContext.Current is the request being routed - UrlRoutingModule
			// runs inside the pipeline, not ahead of it.
			SetRouteContext (HttpContext.Current, rc);

			IHttpHandler h;

			Dictionary <RouteContext, IHttpHandler> handlers = Handlers;
			if (handlers.TryGetValue (rc, out h))
				return h;
			h = CreateHandler (dr, mt, action);
			handlers.Add (rc, h);
			return h;
		}

		static RouteContext MakeRouteContext (RequestContext context, DynamicDataRoute route, string action, MetaTable table)
		{
			RouteData rd = null;
			
			if (route == null) {
				rd = context.RouteData;
				route = rd.Route as DynamicDataRoute;
			}

			if (route != null) {
				if (action == null) {
					if (rd == null)
						rd = context.RouteData;
					action = route.GetActionFromRouteData (rd);
				}
			
				if (table == null) {
					if (rd == null)
						rd = context.RouteData;
				
					table = route.GetTableFromRouteData (rd);
				}
			}
			
			return new RouteContext () {
				Route = route,
				Action = action,
				Table = table,
				Context = context};
		}
		
		sealed class RouteContext
		{
			public DynamicDataRoute Route;
			public string Action;
			public MetaTable Table;
			public RequestContext Context;

			public RouteContext ()
			{
			}
			
			public override bool Equals (object obj)
			{
				RouteContext other = obj as RouteContext;
				return other.Route == Route & other.Action == Action && other.Table == Table && other.Context == Context;
			}

			public override int GetHashCode ()
			{
				return (Route != null ? Route.GetHashCode () << 27 : 0) +
					(Action != null ? Action.GetHashCode () << 19 : 0) +
					(Table != null ? Table.GetHashCode () << 9 : 0) +
					(Context != null ? Context.GetHashCode () : 0);
			}
		}
	}
}
