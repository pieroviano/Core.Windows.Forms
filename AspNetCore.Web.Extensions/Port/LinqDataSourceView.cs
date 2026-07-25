//
// The view behind LinqDataSource: turning markup into an IQueryable and back.
//
// The whole job is four steps - resolve the queryable off the context, filter it, order it, page it -
// plus insert/update/delete through whatever change-tracking the context happens to have.
//
// Change tracking, and why it is done by convention
// -------------------------------------------------
// LINQ to SQL had ITable.Attach / DataContext.SubmitChanges. EF Core has DbContext.Add / Remove /
// SaveChanges, and nothing in System.Web may reference EF Core - the port would then force a database
// stack on every application that uses a GridView.
//
// So the context is called by CONVENTION, through reflection: a method named Add/Remove/Update on the
// queryable's owner or on the context, then SaveChanges. That is exactly the shape of EF Core, of a
// repository, and of most hand-rolled unit-of-work classes. When a context has none of them, the
// failure names the method it looked for rather than throwing NullReferenceException somewhere below.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace System.Web.UI.WebControls
{
	public class LinqDataSourceView : DataSourceView, IStateManager
	{
		readonly LinqDataSource owner;
		ParameterCollection where, orderBy, insert, update, delete;
		bool tracking;

		public LinqDataSourceView (LinqDataSource owner, string name)
			: base (owner, name)
		{
			this.owner = owner;
		}

		public override bool CanPage {
			get { return true; }
		}

		public override bool CanSort {
			get { return true; }
		}

		public override bool CanRetrieveTotalRowCount {
			get { return true; }
		}

		public override bool CanInsert {
			get { return owner.EnableInsert; }
		}

		public override bool CanUpdate {
			get { return owner.EnableUpdate; }
		}

		public override bool CanDelete {
			get { return owner.EnableDelete; }
		}

		public ParameterCollection WhereParameters {
			get { return where ?? (where = Parameters ()); }
		}

		public ParameterCollection OrderByParameters {
			get { return orderBy ?? (orderBy = Parameters ()); }
		}

		public ParameterCollection InsertParameters {
			get { return insert ?? (insert = Parameters ()); }
		}

		public ParameterCollection UpdateParameters {
			get { return update ?? (update = Parameters ()); }
		}

		public ParameterCollection DeleteParameters {
			get { return delete ?? (delete = Parameters ()); }
		}

		ParameterCollection Parameters ()
		{
			var collection = new ParameterCollection ();
			collection.ParametersChanged += (s, e) => OnDataSourceViewChanged (EventArgs.Empty);

			if (tracking)
				((IStateManager) collection).TrackViewState ();

			return collection;
		}

		// -----------------------------------------------------------------------------------------
		// Select
		// -----------------------------------------------------------------------------------------

		protected internal override IEnumerable ExecuteSelect (DataSourceSelectArguments arguments)
		{
			object context = null;

			try {
				context = owner.CreateContext ();
				IQueryable queryable = ResolveQueryable (context);

				IDictionary<string, object> whereValues = Evaluate (WhereParameters);

				queryable = ApplyWhere (queryable, whereValues);

				// Total row count BEFORE paging, and only when asked: it is a second round trip to the
				// database, and a GridView that is not showing a pager does not need it.
				if (arguments.RetrieveTotalRowCount)
					arguments.TotalRowCount = QueryableHelper.Count (queryable);

				queryable = ApplyOrderBy (queryable, arguments);
				queryable = ApplyPaging (queryable, arguments);
				queryable = ApplySelect (queryable);

				// Materialised HERE, inside the try, so a failure is reported through Selected with the
				// context still alive - rather than surfacing later from the data-bound control, by
				// which time the context has been disposed and the stack trace says nothing useful.
				var results = new List<object> ();
				foreach (object item in queryable)
					results.Add (item);

				var status = new LinqDataSourceStatusEventArgs (results, null) {
					TotalRowCount = arguments.TotalRowCount,
				};
				owner.RaiseSelected (status);

				return results;
			} catch (Exception e) {
				var status = new LinqDataSourceStatusEventArgs (null, e);
				owner.RaiseSelected (status);

				if (!status.ExceptionHandled)
					throw;

				return Array.Empty<object> ();
			} finally {
				(context as IDisposable)?.Dispose ();
			}
		}

		/// <summary>
		/// The raw member value behind the queryable - a DbSet, a List, whatever the context exposes.
		/// </summary>
		/// <remarks>
		/// Kept because ResolveQueryable may WRAP it: a List&lt;T&gt; becomes an EnumerableQuery, and an
		/// EnumerableQuery has no Add or Remove. Change tracking has to be offered the original, or
		/// inserting into a context whose member is an ordinary collection fails with "could not find an
		/// Add method" while looking straight at one.
		/// </remarks>
		object rawCollection;

		IQueryable ResolveQueryable (object context)
		{
			if (String.IsNullOrEmpty (owner.TableName))
				throw new InvalidOperationException (
					"LinqDataSource '" + owner.ID + "' has no TableName. Name the property on " +
					context.GetType ().FullName + " that holds the data - a DbSet, or any IQueryable.");

			Type type = context.GetType ();

			PropertyInfo property = type.GetProperty (owner.TableName,
								  BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
			object value = property?.GetValue (context);

			if (value == null) {
				FieldInfo field = type.GetField (owner.TableName,
								 BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
				value = field?.GetValue (context);
			}

			if (value == null)
				throw new InvalidOperationException (
					"LinqDataSource '" + owner.ID + "': " + type.FullName + " has no member named '" +
					owner.TableName + "'. Available queryable members: " +
					String.Join (", ", QueryableHelper.QueryableMemberNames (type)));

			rawCollection = value;

			var queryable = value as IQueryable;
			if (queryable == null) {
				// An IEnumerable is fine and common - an in-memory repository, a List<T> property.
				var enumerable = value as IEnumerable;
				if (enumerable == null)
					throw new InvalidOperationException (
						"LinqDataSource '" + owner.ID + "': " + type.FullName + "." + owner.TableName +
						" is a " + value.GetType ().FullName + ", which is neither IQueryable nor IEnumerable.");

				queryable = QueryableHelper.AsQueryable (enumerable);
			}

			return queryable;
		}

		IQueryable ApplyWhere (IQueryable queryable, IDictionary<string, object> values)
		{
			if (owner.AutoGenerateWhereClause) {
				// Every parameter becomes an equality test against the property of the same name, and a
				// null one is SKIPPED rather than compared - that is what makes an optional filter on a
				// search page work without the markup saying so.
				foreach (KeyValuePair<string, object> pair in values) {
					if (pair.Value == null)
						continue;

					queryable = DynamicQuery.Where (queryable, pair.Key + " == @" + pair.Key,
									new Dictionary<string, object> { [pair.Key] = pair.Value });
				}

				return queryable;
			}

			return String.IsNullOrEmpty (owner.Where)
				? queryable
				: DynamicQuery.Where (queryable, owner.Where, values);
		}

		IQueryable ApplyOrderBy (IQueryable queryable, DataSourceSelectArguments arguments)
		{
			// The control's own sort expression wins: it is the column header the user just clicked,
			// and OrderBy in the markup is the default they clicked away from.
			if (owner.AutoSort && !String.IsNullOrEmpty (arguments.SortExpression))
				return DynamicQuery.OrderBy (queryable, arguments.SortExpression);

			if (owner.AutoGenerateOrderByClause) {
				string generated = String.Join (", ", Evaluate (OrderByParameters)
					.Where (p => p.Value != null)
					.Select (p => Convert.ToString (p.Value, CultureInfo.InvariantCulture)));

				return String.IsNullOrEmpty (generated) ? queryable : DynamicQuery.OrderBy (queryable, generated);
			}

			return String.IsNullOrEmpty (owner.OrderBy)
				? queryable
				: DynamicQuery.OrderBy (queryable, owner.OrderBy);
		}

		IQueryable ApplyPaging (IQueryable queryable, DataSourceSelectArguments arguments)
		{
			if (!owner.AutoPage || arguments.MaximumRows <= 0)
				return queryable;

			// Skip needs an ordered source to be meaningful; an unordered one gives the database
			// licence to return different rows for the same page. Order by the first property when the
			// markup did not say - arbitrary, but STABLE, which is the property that matters.
			if (String.IsNullOrEmpty (owner.OrderBy) && String.IsNullOrEmpty (arguments.SortExpression))
				queryable = DynamicQuery.OrderByFirstPropertyIfUnordered (queryable);

			return QueryableHelper.Page (queryable, arguments.StartRowIndex, arguments.MaximumRows);
		}

		IQueryable ApplySelect (IQueryable queryable)
		{
			return String.IsNullOrEmpty (owner.Select)
				? queryable
				: DynamicQuery.Select (queryable, owner.Select);
		}

		// -----------------------------------------------------------------------------------------
		// Insert / Update / Delete
		// -----------------------------------------------------------------------------------------

		protected override int ExecuteInsert (IDictionary values)
		{
			if (!CanInsert)
				throw new NotSupportedException ("LinqDataSource '" + owner.ID + "' has EnableInsert=\"false\".");

			return Mutate ((context, queryable) => {
				object entity = Activator.CreateInstance (queryable.ElementType);
				EntityBinder.Apply (entity, Merge (Evaluate (InsertParameters), values));

				ChangeTracker.Add (context, rawCollection, queryable, entity);
			});
		}

		protected override int ExecuteDelete (IDictionary keys, IDictionary oldValues)
		{
			if (!CanDelete)
				throw new NotSupportedException ("LinqDataSource '" + owner.ID + "' has EnableDelete=\"false\".");

			return Mutate ((context, queryable) => {
				object entity = FindByKeys (queryable, keys, oldValues);
				ChangeTracker.Remove (context, rawCollection, queryable, entity);
			});
		}

		protected override int ExecuteUpdate (IDictionary keys, IDictionary values, IDictionary oldValues)
		{
			if (!CanUpdate)
				throw new NotSupportedException ("LinqDataSource '" + owner.ID + "' has EnableUpdate=\"false\".");

			return Mutate ((context, queryable) => {
				object entity = FindByKeys (queryable, keys, oldValues);
				EntityBinder.Apply (entity, Merge (Evaluate (UpdateParameters), values));

				ChangeTracker.Update (context, rawCollection, queryable, entity);
			});
		}

		int Mutate (Action<object, IQueryable> change)
		{
			object context = null;

			try {
				context = owner.CreateContext ();
				IQueryable queryable = ResolveQueryable (context);

				change (context, queryable);

				return ChangeTracker.SaveChanges (context);
			} finally {
				(context as IDisposable)?.Dispose ();
			}
		}

		/// <summary>
		/// Locates the row a GridView is editing. Keys first - that is what DataKeyNames produces -
		/// falling back to the old values when the control supplied no keys.
		/// </summary>
		object FindByKeys (IQueryable queryable, IDictionary keys, IDictionary oldValues)
		{
			IDictionary source = keys != null && keys.Count > 0 ? keys : oldValues;

			if (source == null || source.Count == 0)
				throw new InvalidOperationException (
					"LinqDataSource '" + owner.ID + "' cannot identify the row to change: neither keys nor " +
					"old values were supplied. Set DataKeyNames on the data-bound control.");

			IQueryable filtered = queryable;
			var values = new Dictionary<string, object> ();
			var clauses = new List<string> ();

			foreach (DictionaryEntry entry in source) {
				string name = Convert.ToString (entry.Key, CultureInfo.InvariantCulture);
				values [name] = entry.Value;
				clauses.Add (name + " == @" + name);
			}

			filtered = DynamicQuery.Where (filtered, String.Join (" && ", clauses), values);

			object found = null;
			int count = 0;

			foreach (object item in filtered) {
				found = item;
				if (++count > 1)
					break;
			}

			if (count == 0)
				throw new InvalidOperationException (
					"LinqDataSource '" + owner.ID + "': no row matched the supplied keys. It may have been " +
					"deleted by someone else since the page was rendered.");

			if (count > 1)
				throw new InvalidOperationException (
					"LinqDataSource '" + owner.ID + "': the supplied keys matched more than one row. " +
					"DataKeyNames has to name a unique key.");

			return found;
		}

		static IDictionary<string, object> Merge (IDictionary<string, object> parameters, IDictionary values)
		{
			var merged = new Dictionary<string, object> (parameters, StringComparer.OrdinalIgnoreCase);

			// The control's values win: parameters are the defaults, the bound fields are what the user
			// actually typed.
			if (values != null) {
				foreach (DictionaryEntry entry in values)
					merged [Convert.ToString (entry.Key, CultureInfo.InvariantCulture)] = entry.Value;
			}

			return merged;
		}

		IDictionary<string, object> Evaluate (ParameterCollection parameters)
		{
			var values = new Dictionary<string, object> (StringComparer.OrdinalIgnoreCase);

			if (parameters == null)
				return values;

			IOrderedDictionary evaluated = parameters.GetValues (
				HttpContext.Current, owner);

			foreach (DictionaryEntry entry in evaluated)
				values [Convert.ToString (entry.Key, CultureInfo.InvariantCulture)] = entry.Value;

			return values;
		}

		// -----------------------------------------------------------------------------------------
		// IStateManager - the parameter collections carry view state
		// -----------------------------------------------------------------------------------------

		bool IStateManager.IsTrackingViewState {
			get { return tracking; }
		}

		void IStateManager.TrackViewState ()
		{
			tracking = true;

			((IStateManager) WhereParameters).TrackViewState ();
			((IStateManager) OrderByParameters).TrackViewState ();
			((IStateManager) InsertParameters).TrackViewState ();
			((IStateManager) UpdateParameters).TrackViewState ();
			((IStateManager) DeleteParameters).TrackViewState ();
		}

		object IStateManager.SaveViewState ()
		{
			return new object [] {
				((IStateManager) WhereParameters).SaveViewState (),
				((IStateManager) OrderByParameters).SaveViewState (),
				((IStateManager) InsertParameters).SaveViewState (),
				((IStateManager) UpdateParameters).SaveViewState (),
				((IStateManager) DeleteParameters).SaveViewState (),
			};
		}

		void IStateManager.LoadViewState (object state)
		{
			if (state == null)
				return;

			var parts = (object []) state;
			((IStateManager) WhereParameters).LoadViewState (parts [0]);
			((IStateManager) OrderByParameters).LoadViewState (parts [1]);
			((IStateManager) InsertParameters).LoadViewState (parts [2]);
			((IStateManager) UpdateParameters).LoadViewState (parts [3]);
			((IStateManager) DeleteParameters).LoadViewState (parts [4]);
		}
	}
}
