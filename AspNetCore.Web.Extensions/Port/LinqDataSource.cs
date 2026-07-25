//
// LinqDataSource, retargeted from LINQ to SQL onto IQueryable.
//
// Why this is written rather than ported
// -------------------------------------
// Upstream's LinqDataSourceView is in the tree, and it is built AROUND LINQ to SQL rather than merely
// referencing it: ITable, DataContext, MetaDataMember and the attach/submit change-tracking model
// appear in fifteen places, including the metadata walk it uses to work out primary keys. LINQ to SQL
// does not exist on .NET and is not coming, so compiling that file would mean shimming a change-
// tracking model whose semantics EF Core expresses completely differently - faking Attach and
// SubmitChanges over something that does not work that way.
//
// So the markup surface is reproduced against IQueryable instead. An .aspx that says
//
//     <asp:LinqDataSource ID="Products" runat="server"
//                         ContextTypeName="MyApp.ShopContext" TableName="Products"
//                         Where="CategoryId == @Category" OrderBy="Name"
//                         EnableUpdate="true" EnableDelete="true" EnableInsert="true">
//       <WhereParameters><asp:QueryStringParameter Name="Category" QueryStringField="cat" Type="Int32" /></WhereParameters>
//     </asp:LinqDataSource>
//
// keeps working, and the context it names can now be an EF Core DbContext, a repository exposing
// IQueryable properties, or anything else with a queryable member of that name.
//
// What that costs, stated plainly: the LINQ-to-SQL-specific pieces have no equivalent and are gone -
// StoreOriginalValuesInViewState optimistic concurrency against a DataContext, and ContextTypeName
// pointing at a DataContext subclass. Both are reported by port-project when it sees them.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace System.Web.UI.WebControls
{
	/// <summary>
	/// Data source over an <see cref="IQueryable"/> exposed by a context object.
	/// </summary>
	[PersistChildren (false)]
	[ParseChildren (true)]
	public class LinqDataSource : DataSourceControl
	{
		LinqDataSourceView view;

		/// <summary>Type name of the context - an EF Core DbContext, or anything with queryable members.</summary>
		public virtual string ContextTypeName { get; set; }

		/// <summary>Property on the context holding the queryable, e.g. a DbSet name.</summary>
		public virtual string TableName { get; set; }

		/// <summary>A filter, in the dynamic-query syntax: <c>"CategoryId == @Category &amp;&amp; Price &gt; 10"</c>.</summary>
		public virtual string Where { get; set; }

		public virtual string OrderBy { get; set; }

		/// <summary>Projection, e.g. <c>"new (Id, Name)"</c>. Empty selects the entity itself.</summary>
		public virtual string Select { get; set; }

		public virtual bool AutoPage { get; set; } = true;

		public virtual bool AutoSort { get; set; } = true;

		/// <summary>
		/// Build the filter from WhereParameters alone, comparing each to the property of the same
		/// name, instead of from the Where expression.
		/// </summary>
		public virtual bool AutoGenerateWhereClause { get; set; }

		public virtual bool AutoGenerateOrderByClause { get; set; }

		public virtual bool EnableInsert { get; set; }

		public virtual bool EnableUpdate { get; set; }

		public virtual bool EnableDelete { get; set; }

		[PersistenceMode (PersistenceMode.InnerProperty)]
		public ParameterCollection WhereParameters {
			get { return View.WhereParameters; }
		}

		[PersistenceMode (PersistenceMode.InnerProperty)]
		public ParameterCollection OrderByParameters {
			get { return View.OrderByParameters; }
		}

		[PersistenceMode (PersistenceMode.InnerProperty)]
		public ParameterCollection InsertParameters {
			get { return View.InsertParameters; }
		}

		[PersistenceMode (PersistenceMode.InnerProperty)]
		public ParameterCollection UpdateParameters {
			get { return View.UpdateParameters; }
		}

		[PersistenceMode (PersistenceMode.InnerProperty)]
		public ParameterCollection DeleteParameters {
			get { return View.DeleteParameters; }
		}

		/// <summary>Raised before the context is created, so an application can supply its own.</summary>
		public event EventHandler<LinqDataSourceContextEventArgs> ContextCreating;

		/// <summary>Raised after the query has run, carrying the exception if it failed.</summary>
		public event EventHandler<LinqDataSourceStatusEventArgs> Selected;

		internal LinqDataSourceView View {
			get {
				if (view == null) {
					view = new LinqDataSourceView (this, "DefaultView");
					if (IsTrackingViewState)
						((IStateManager) view).TrackViewState ();
				}

				return view;
			}
		}

		protected override DataSourceView GetView (string viewName)
		{
			return View;
		}

		protected override ICollection GetViewNames ()
		{
			return new [] { "DefaultView" };
		}

		internal object CreateContext ()
		{
			var args = new LinqDataSourceContextEventArgs ();
			ContextCreating?.Invoke (this, args);

			if (args.ObjectInstance != null)
				return args.ObjectInstance;

			if (String.IsNullOrEmpty (ContextTypeName))
				throw new InvalidOperationException (
					"LinqDataSource '" + ID + "' has no ContextTypeName, and nothing supplied an instance " +
					"from the ContextCreating event.");

			Type type = HttpApplication.LoadType (ContextTypeName, true);
			return Activator.CreateInstance (type);
		}

		internal void RaiseSelected (LinqDataSourceStatusEventArgs args)
		{
			Selected?.Invoke (this, args);
		}
	}

	public class LinqDataSourceContextEventArgs : CancelEventArgs
	{
		/// <summary>Set this to supply the context yourself - an injected DbContext, typically.</summary>
		public object ObjectInstance { get; set; }
	}

	public class LinqDataSourceStatusEventArgs : EventArgs
	{
		public LinqDataSourceStatusEventArgs (object result, Exception exception)
		{
			Result = result;
			Exception = exception;
		}

		public object Result { get; }

		public Exception Exception { get; }

		/// <summary>Set to true to swallow <see cref="Exception"/> rather than let it propagate.</summary>
		public bool ExceptionHandled { get; set; }

		public int TotalRowCount { get; set; }
	}
}
