//
// Dynamic Data's model, read off an IQueryable context instead of a LINQ to SQL DataContext.
//
// Dynamic Data itself is data-layer agnostic: it consumes an abstract DataModelProvider, whose tables
// are TableProviders, whose columns are ColumnProviders. Upstream ships exactly one implementation of
// that triple - DLinq*, over System.Data.Linq - and that is the only reason Dynamic Data could not be
// ported. Replace the triple and the scaffolding, the field templates, the routing and the metadata
// attributes all work unchanged.
//
// What a "context" is here
// -----------------------
// Any object with public members that are IQueryable or IEnumerable. An EF Core DbContext is one (its
// DbSet<T> properties), and so is a repository, and so is a class with a few List<T> in it. Nothing is
// referenced from EF Core - the port must not force a database stack on an application that just wants
// a GridView - so the shape is found by reflection.
//
// Keys, and why convention rather than configuration
// --------------------------------------------------
// LINQ to SQL knew primary keys from its mapping attributes. Here the key is found the way every
// scaffolding tool since has done it: [Key] if present, else a property named Id, else one named
// <Type>Id. When none of those hold, the table is still listed but reports no primary key, and Dynamic
// Data will scaffold a list page without edit links rather than fail - which is the useful failure.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Web.DynamicData.ModelProviders;

namespace System.Web.DynamicData.ModelProviders
{
	/// <summary>
	/// A Dynamic Data model over any context exposing <see cref="IQueryable"/> members.
	/// </summary>
	public class QueryableDataModelProvider : DataModelProvider
	{
		readonly Func<object> contextFactory;
		readonly List<TableProvider> tables = new List<TableProvider> ();

		public QueryableDataModelProvider (Func<object> contextFactory)
		{
			this.contextFactory = contextFactory ?? throw new ArgumentNullException (nameof (contextFactory));

			object probe = contextFactory ();
			if (probe == null)
				throw new InvalidOperationException ("The context factory returned null.");

			ContextType = probe.GetType ();

			foreach (MemberInfo member in QueryableMembers (ContextType))
				tables.Add (new QueryableTableProvider (this, member));

			(probe as IDisposable)?.Dispose ();
		}

		public override object CreateContext ()
		{
			return contextFactory ();
		}

		public override ReadOnlyCollection<TableProvider> Tables {
			get { return tables.AsReadOnly (); }
		}

		/// <summary>
		/// Public members whose type is a queryable of something. String is excluded explicitly - it is
		/// IEnumerable&lt;char&gt;, and a context with a Name property should not scaffold a table of
		/// characters.
		/// </summary>
		internal static IEnumerable<MemberInfo> QueryableMembers (Type contextType)
		{
			foreach (PropertyInfo property in contextType.GetProperties (BindingFlags.Public | BindingFlags.Instance)) {
				if (ElementTypeOf (property.PropertyType) != null)
					yield return property;
			}

			foreach (FieldInfo field in contextType.GetFields (BindingFlags.Public | BindingFlags.Instance)) {
				if (ElementTypeOf (field.FieldType) != null)
					yield return field;
			}
		}

		internal static Type ElementTypeOf (Type type)
		{
			if (type == typeof (string) || type.IsPrimitive)
				return null;

			foreach (Type contract in type.GetInterfaces ().Concat (new [] { type })) {
				if (contract.IsGenericType && contract.GetGenericTypeDefinition () == typeof (IEnumerable<>))
					return contract.GetGenericArguments () [0];
			}

			return null;
		}
	}

	sealed class QueryableTableProvider : TableProvider
	{
		readonly MemberInfo member;
		readonly List<ColumnProvider> columns = new List<ColumnProvider> ();

		public QueryableTableProvider (DataModelProvider model, MemberInfo member)
			: base (model)
		{
			this.member = member;

			Type element = QueryableDataModelProvider.ElementTypeOf (MemberType (member));

			Name = member.Name;
			EntityType = element;

			PropertyInfo key = FindKey (element);

			foreach (PropertyInfo property in element.GetProperties (BindingFlags.Public | BindingFlags.Instance)) {
				if (!property.CanRead)
					continue;

				columns.Add (new QueryableColumnProvider (this, property, isPrimaryKey: property == key));
			}
		}

		public override ReadOnlyCollection<ColumnProvider> Columns {
			get { return columns.AsReadOnly (); }
		}

		public override IQueryable GetQuery (object context)
		{
			object value = member is PropertyInfo property
				? property.GetValue (context)
				: ((FieldInfo) member).GetValue (context);

			if (value == null)
				return null;

			var queryable = value as IQueryable;
			if (queryable != null)
				return queryable;

			// An in-memory collection is a legitimate context member - a lookup table, a test double.
			var enumerable = (IEnumerable) value;
			Type element = QueryableDataModelProvider.ElementTypeOf (MemberType (member));

			return (IQueryable) typeof (Queryable).GetMethods ()
				.First (m => m.Name == "AsQueryable" && m.IsGenericMethod)
				.MakeGenericMethod (element)
				.Invoke (null, new [] { value });
		}

		static Type MemberType (MemberInfo member)
		{
			return member is PropertyInfo property ? property.PropertyType : ((FieldInfo) member).FieldType;
		}

		/// <summary>
		/// [Key], then "Id", then "&lt;Type&gt;Id" - the same order every scaffolding tool has used
		/// since LINQ to SQL stopped being the only answer. Null when none matches, which leaves the
		/// table listable but not editable rather than failing the whole model.
		/// </summary>
		static PropertyInfo FindKey (Type entity)
		{
			PropertyInfo [] properties = entity.GetProperties (BindingFlags.Public | BindingFlags.Instance);

			PropertyInfo annotated = properties.FirstOrDefault (
				p => p.GetCustomAttribute<KeyAttribute> () != null);
			if (annotated != null)
				return annotated;

			return properties.FirstOrDefault (p => p.Name.Equals ("Id", StringComparison.OrdinalIgnoreCase))
			       ?? properties.FirstOrDefault (
					p => p.Name.Equals (entity.Name + "Id", StringComparison.OrdinalIgnoreCase));
		}
	}

	sealed class QueryableColumnProvider : ColumnProvider
	{
		public QueryableColumnProvider (TableProvider table, PropertyInfo property, bool isPrimaryKey)
			: base (table)
		{
			Name = property.Name;
			ColumnType = property.PropertyType;
			IsPrimaryKey = isPrimaryKey;
			EntityTypeProperty = property;

			// A read-only property is scaffolded but not editable. ColumnProvider has no IsReadOnly, so
			// it is expressed as "generated" - which is what Dynamic Data checks before offering a field
			// for edit, and is true of a computed column either way.
			IsSortable = true;
			Nullable = !property.PropertyType.IsValueType ||
				   System.Nullable.GetUnderlyingType (property.PropertyType) != null;

			// A key that the store generates must not be scaffolded as an editable field, or the insert
			// page offers to set it and the database rejects the row.
			IsGenerated = !property.CanWrite || (isPrimaryKey && IsAutoGenerated (property));

			MaxLength = property.GetCustomAttribute<StringLengthAttribute> ()?.MaximumLength
				    ?? property.GetCustomAttribute<MaxLengthAttribute> ()?.Length
				    ?? 0;
		}

		static bool IsAutoGenerated (PropertyInfo property)
		{
			var generated = property.GetCustomAttribute<DatabaseGeneratedAttribute> ();
			if (generated != null)
				return generated.DatabaseGeneratedOption != DatabaseGeneratedOption.None;

			// No attribute: an integral key is assumed to be store-generated, which is the EF Core
			// convention and true of essentially every table with an int Id.
			Type type = System.Nullable.GetUnderlyingType (property.PropertyType) ?? property.PropertyType;
			return type == typeof (int) || type == typeof (long) || type == typeof (Guid);
		}
	}
}
