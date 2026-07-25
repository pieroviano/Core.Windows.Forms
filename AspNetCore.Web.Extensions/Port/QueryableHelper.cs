//
// The non-generic corners of IQueryable, and change tracking by convention.
//
// LinqDataSource knows its element type only at runtime, so every Queryable call here goes through
// reflection or through a MakeGenericMethod. Kept in one place so the view reads as intent.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace System.Web.UI.WebControls
{
	static class QueryableHelper
	{
		public static int Count (IQueryable source)
		{
			return (int) typeof (Queryable).GetMethods ()
				.First (m => m.Name == "Count" && m.GetParameters ().Length == 1)
				.MakeGenericMethod (source.ElementType)
				.Invoke (null, new object [] { source });
		}

		public static IQueryable Page (IQueryable source, int startRowIndex, int maximumRows)
		{
			if (startRowIndex > 0)
				source = Call (source, "Skip", startRowIndex);

			if (maximumRows > 0)
				source = Call (source, "Take", maximumRows);

			return source;
		}

		static IQueryable Call (IQueryable source, string method, int count)
		{
			return (IQueryable) typeof (Queryable).GetMethods ()
				.First (m => m.Name == method && m.GetParameters ().Length == 2)
				.MakeGenericMethod (source.ElementType)
				.Invoke (null, new object [] { source, count });
		}

		/// <summary>
		/// Wraps a plain IEnumerable as IQueryable, inferring the element type from what is in it.
		/// </summary>
		/// <remarks>
		/// An in-memory List&lt;T&gt; property is a perfectly ordinary thing for a context to expose - a
		/// test double, a small lookup table - and refusing it would make LinqDataSource untestable
		/// without a database.
		/// </remarks>
		public static IQueryable AsQueryable (IEnumerable source)
		{
			Type element = ElementTypeOf (source);

			return (IQueryable) typeof (Queryable).GetMethods ()
				.First (m => m.Name == "AsQueryable" && m.IsGenericMethod)
				.MakeGenericMethod (element)
				.Invoke (null, new object [] { Cast (source, element) });
		}

		static object Cast (IEnumerable source, Type element)
		{
			return typeof (Enumerable).GetMethod ("Cast").MakeGenericMethod (element)
				.Invoke (null, new object [] { source });
		}

		static Type ElementTypeOf (IEnumerable source)
		{
			foreach (Type contract in source.GetType ().GetInterfaces ()) {
				if (contract.IsGenericType && contract.GetGenericTypeDefinition () == typeof (IEnumerable<>))
					return contract.GetGenericArguments () [0];
			}

			// Non-generic: take the type of the first item. Empty and non-generic means there is nothing
			// to infer from and nothing to show either.
			foreach (object item in source) {
				if (item != null)
					return item.GetType ();
			}

			return typeof (object);
		}

		public static IEnumerable<string> QueryableMemberNames (Type contextType)
		{
			var names = contextType.GetProperties (BindingFlags.Public | BindingFlags.Instance)
				.Where (p => typeof (IEnumerable).IsAssignableFrom (p.PropertyType) &&
					     p.PropertyType != typeof (string))
				.Select (p => p.Name)
				.ToArray ();

			return names.Length > 0 ? names : new [] { "(none)" };
		}
	}

	/// <summary>
	/// Add / Remove / Update / SaveChanges, found by convention on the context or the collection.
	/// </summary>
	/// <remarks>
	/// By convention rather than against an interface, because the alternative is referencing EF Core
	/// from System.Web.Extensions - which would force a database stack on every application that uses a
	/// GridView. The shape looked for is EF Core's, which is also a repository's and most hand-rolled
	/// unit-of-work classes'.
	///
	/// When nothing matches, the exception names the methods that were looked for and where. That is the
	/// difference between "your context needs a SaveChanges method" and a NullReferenceException from
	/// inside a data-bound control.
	/// </remarks>
	static class ChangeTracker
	{
		// The order of candidates matters. `collection` is the RAW member off the context - a DbSet, a
		// List<T> - and it is tried first because it is the thing that actually owns the rows. `queryable`
		// may be a wrapper around it (a List<T> becomes an EnumerableQuery, which has no Add at all), and
		// `context` is the last resort for a repository that exposes Add/Remove at the top level.
		public static void Add (object context, object collection, IQueryable queryable, object entity)
		{
			if (TryInvoke (collection, new [] { "Add", "InsertOnSubmit" }, entity))
				return;

			if (TryInvoke (queryable, new [] { "Add", "InsertOnSubmit" }, entity))
				return;

			if (TryInvoke (context, new [] { "Add", "Insert" }, entity))
				return;

			throw Missing (context, queryable, "Add", "InsertOnSubmit");
		}

		public static void Remove (object context, object collection, IQueryable queryable, object entity)
		{
			if (TryInvoke (collection, new [] { "Remove", "DeleteOnSubmit" }, entity))
				return;

			if (TryInvoke (queryable, new [] { "Remove", "DeleteOnSubmit" }, entity))
				return;

			if (TryInvoke (context, new [] { "Remove", "Delete" }, entity))
				return;

			throw Missing (context, queryable, "Remove", "DeleteOnSubmit");
		}

		public static void Update (object context, object collection, IQueryable queryable, object entity)
		{
			// EF Core tracks the entity it handed out, so mutating it is usually enough and Update is
			// optional. Absence is therefore NOT an error here, unlike Add and Remove - and an in-memory
			// List<T> needs nothing at all, because FindByKeys returned the very object it holds.
			if (TryInvoke (collection, new [] { "Update" }, entity))
				return;

			if (TryInvoke (queryable, new [] { "Update" }, entity))
				return;

			TryInvoke (context, new [] { "Update" }, entity);
		}

		public static int SaveChanges (object context)
		{
			MethodInfo save = Find (context, new [] { "SaveChanges", "SubmitChanges" }, parameterCount: 0);

			if (save == null)
				throw new InvalidOperationException (
					context.GetType ().FullName + " has no SaveChanges () or SubmitChanges () method, so " +
					"LinqDataSource cannot commit. Add one, or handle the Inserting/Updating/Deleting " +
					"events and do the work yourself.");

			object result = save.Invoke (context, Array.Empty<object> ());
			return result is int affected ? affected : 1;
		}

		static bool TryInvoke (object target, string [] names, object entity)
		{
			if (target == null)
				return false;

			MethodInfo method = Find (target, names, parameterCount: 1);
			if (method == null)
				return false;

			ParameterInfo parameter = method.GetParameters () [0];
			if (!parameter.ParameterType.IsInstanceOfType (entity))
				return false;

			method.Invoke (target, new [] { entity });
			return true;
		}

		static MethodInfo Find (object target, string [] names, int parameterCount)
		{
			foreach (string name in names) {
				MethodInfo method = target.GetType ()
					.GetMethods (BindingFlags.Public | BindingFlags.Instance)
					.FirstOrDefault (m => m.Name == name && m.GetParameters ().Length == parameterCount);

				if (method != null)
					return method;
			}

			return null;
		}

		static InvalidOperationException Missing (object context, IQueryable queryable, params string [] names)
		{
			return new InvalidOperationException (
				"LinqDataSource could not find a " + String.Join (" or ", names) + " method taking a " +
				queryable.ElementType.Name + ", on either " + queryable.GetType ().Name + " or " +
				context.GetType ().FullName + ". EF Core's DbSet has one; a custom context needs the same " +
				"shape, or handle the Inserting/Deleting events and do the work yourself.");
		}
	}

	/// <summary>Copies posted values onto an entity, converting to each member's type.</summary>
	static class EntityBinder
	{
		public static void Apply (object entity, IDictionary<string, object> values)
		{
			Type type = entity.GetType ();

			foreach (KeyValuePair<string, object> pair in values) {
				PropertyInfo property = type.GetProperty (
					pair.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

				if (property == null || !property.CanWrite)
					continue;

				property.SetValue (entity, Coerce (pair.Value, property.PropertyType));
			}
		}

		static object Coerce (object value, Type target)
		{
			if (value == null || value == DBNull.Value)
				return target.IsValueType && Nullable.GetUnderlyingType (target) == null
					? Activator.CreateInstance (target)
					: null;

			Type underlying = Nullable.GetUnderlyingType (target) ?? target;

			if (underlying.IsInstanceOfType (value))
				return value;

			// An empty posted field is absence, not zero: a GridView renders "" for a null column, and
			// converting that to 0 would silently overwrite the null on the way back.
			string text = value as string;
			if (text != null && text.Length == 0)
				return Nullable.GetUnderlyingType (target) != null || !target.IsValueType
					? null
					: Activator.CreateInstance (target);

			if (underlying.IsEnum)
				return text != null ? Enum.Parse (underlying, text, true) : Enum.ToObject (underlying, value);

			if (underlying == typeof (Guid))
				return Guid.Parse (Convert.ToString (value, CultureInfo.InvariantCulture));

			return Convert.ChangeType (value, underlying, CultureInfo.InvariantCulture);
		}
	}
}
