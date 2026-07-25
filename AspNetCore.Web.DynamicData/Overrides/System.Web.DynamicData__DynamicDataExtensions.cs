//
// DynamicDataExtensions.cs
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
//
// PORT NOTE - why this is an override
// -----------------------------------
// Two things, and the first is an API that is simply absent.
//
// 1. SetMetaTable / GetMetaTable / TryGetMetaTable do not exist in Mono's copy at all. On .NET
//    Framework they are how a page template tells a data-bound control which table it is showing:
//
//        GridView1.SetMetaTable (table);
//
//    Without them, FindMetaTable can only discover a table by walking up to a DataBoundControl whose
//    DataSourceObject happens to implement IDynamicDataSource - so a grid bound to an ordinary
//    IQueryable finds nothing, and every DynamicControl inside it throws NullReferenceException out of
//    ResolveColumn. That rules out the whole IQueryable model this port is built on, which is the one
//    thing it must support. FindMetaTable now consults the stored table first and falls back to the
//    data source, so both binding styles work.
//
// 2. FormatValue, FormatEditValue and FindFieldTemplate threw NotImplementedException. They are the
//    public entry points to the formatting rules a field template applies, and an application writing
//    its own template is meant to call them rather than reimplement DataFormatString handling.
//
// LoadWithForeignKeys and EnablePersistedSelection still throw: both need association metadata that
// QueryableDataModelProvider does not produce, because it reads IQueryable members rather than a
// relational schema with foreign keys.
//
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Security.Permissions;
using System.Security.Principal;
using System.Web.Caching;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.DynamicData.ModelProviders;

namespace System.Web.DynamicData
{
	[AspNetHostingPermission (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	public static class DynamicDataExtensions
	{
		public static object ConvertEditedValue (this IFieldFormattingOptions formattingOptions, string value)
		{
			// Not a surprise anymore...
			if (formattingOptions == null)
				throw new NullReferenceException ();

			if (String.IsNullOrEmpty (value)) {
				if (formattingOptions.ConvertEmptyStringToNull)
					return null;
			} else {
				string nullDisplayText = formattingOptions.NullDisplayText;
				if (!String.IsNullOrEmpty (nullDisplayText) && String.Compare (value, nullDisplayText, StringComparison.Ordinal) == 0)
					return null;
			}

			return value;
		}

		[MonoTODO]
		public static void EnablePersistedSelection (this BaseDataBoundControl dataBoundControl)
		{
			throw new NotImplementedException ();
		}

		[MonoTODO]
		public static void ExpandDynamicWhereParameters (this IDynamicDataSource dataSource)
		{
			// http://forums.asp.net/p/1396453/3005197.aspx#3005197
		}

		[MonoTODO]
		public static IDynamicDataSource FindDataSourceControl (this Control current)
		{
			var control = current as BaseDataBoundControl;
			if (control == null)
				return null;

			string dataSourceID = control.DataSourceID;
			if (!String.IsNullOrEmpty (dataSourceID))
				return control.DataSource as IDynamicDataSource;
			
			Control namingContainer = control.NamingContainer;
			IDynamicDataSource dds;
			while (namingContainer != null) {
				dds = namingContainer.FindControl (dataSourceID) as IDynamicDataSource;
				if (dds != null)
					return dds;

				namingContainer = namingContainer.NamingContainer;
			}

			return null;
		}

		/// <summary>The field template rendering <paramref name="columnName"/> under this control.</summary>
		public static Control FindFieldTemplate (this Control control, string columnName)
		{
			if (control == null)
				throw new NullReferenceException ();

			if (String.IsNullOrEmpty (columnName))
				return null;

			// Depth-first over the whole subtree rather than the immediate children: a DynamicControl
			// inside a GridView sits under a row, a cell and possibly a template - and the caller knows
			// the column name, not the shape of the tree its data-bound control produced.
			foreach (Control child in control.Controls) {
				var dynamicControl = child as DynamicControl;
				if (dynamicControl != null &&
				    String.Compare (dynamicControl.DataField, columnName, StringComparison.OrdinalIgnoreCase) == 0)
					return dynamicControl.FieldTemplate;

				Control found = FindFieldTemplate (child, columnName);
				if (found != null)
					return found;
			}

			return null;
		}

		public static MetaTable FindMetaTable (this Control current)
		{
			// .NET doesn't perform the check, we will
			if (current == null)
				throw new NullReferenceException ();

			while (current != null) {
				// port: the explicitly-set table first. A grid bound to a plain IQueryable has no
				// IDynamicDataSource to interrogate, and that is the normal case for this port.
				MetaTable set;
				if (current.TryGetMetaTable (out set))
					return set;

				DataBoundControl dbc = current as DataBoundControl;
				if (dbc != null) {
					IDynamicDataSource dds = dbc.DataSourceObject as IDynamicDataSource;
					if (dds != null)
						return dds.GetTable ();
				}

				current = current.NamingContainer;
			}

			return null;
		}

		/// <summary>Formats a value the way an EDIT control should show it.</summary>
		public static string FormatEditValue (this IFieldFormattingOptions formattingOptions, object fieldValue)
		{
			if (formattingOptions == null)
				throw new NullReferenceException ();

			// DataFormatString is a DISPLAY format: applying it here turns 49.95 into "$49.95", which the
			// next postback cannot parse back out. ApplyFormatInEditMode exists to opt into that.
			if (!formattingOptions.ApplyFormatInEditMode)
				return fieldValue == null ? String.Empty : Convert.ToString (fieldValue, CultureInfo.CurrentCulture);

			return FormatCore (formattingOptions, fieldValue, encode: false);
		}

		/// <summary>Formats a value the way a READ-ONLY control should show it.</summary>
		public static string FormatValue (this IFieldFormattingOptions formattingOptions, object fieldValue)
		{
			if (formattingOptions == null)
				throw new NullReferenceException ();

			return FormatCore (formattingOptions, fieldValue, formattingOptions.HtmlEncode);
		}

		static string FormatCore (IFieldFormattingOptions options, object fieldValue, bool encode)
		{
			if (fieldValue == null)
				// NullDisplayText is the application's own markup - an em-dash, an icon - so it is never
				// encoded. BoundField follows the same rule.
				return options.NullDisplayText ?? String.Empty;

			string format = options.DataFormatString;
			string formatted = String.IsNullOrEmpty (format)
					   ? Convert.ToString (fieldValue, CultureInfo.CurrentCulture)
					   : String.Format (CultureInfo.CurrentCulture, format, fieldValue);

			if (formatted == null)
				return String.Empty;

			// Encode AFTER formatting, so a format string cannot smuggle markup through.
			return encode ? HttpUtility.HtmlEncode (formatted) : formatted;
		}

		/// <summary>
		/// Records which table a control is showing, so the DynamicControls inside it can resolve their
		/// columns. This is the API a page template calls; it does not exist in the upstream sources.
		/// </summary>
		/// <remarks>
		/// Stored in HttpContext.Items keyed by the control instance. The association is meaningful for
		/// exactly one request - the control tree is rebuilt on the next - so a per-request dictionary is
		/// both the right lifetime and self-cleaning, where a static one keyed by control would leak a
		/// whole control tree per request.
		/// </remarks>
		public static void SetMetaTable (this Control control, MetaTable table)
		{
			if (control == null)
				throw new NullReferenceException ();

			HttpContext context = HttpContext.Current;
			if (context == null)
				return;

			context.Items [new MetaTableKey (control)] = table;
		}

		/// <summary>The table recorded by <see cref="SetMetaTable"/> for this control, if any.</summary>
		public static bool TryGetMetaTable (this Control control, out MetaTable table)
		{
			table = null;

			if (control == null)
				throw new NullReferenceException ();

			HttpContext context = HttpContext.Current;
			if (context == null)
				return false;

			table = context.Items [new MetaTableKey (control)] as MetaTable;
			return table != null;
		}

		/// <summary>As <see cref="TryGetMetaTable"/>, but throws when nothing was recorded.</summary>
		public static MetaTable GetMetaTable (this Control control)
		{
			MetaTable table;
			if (control.TryGetMetaTable (out table))
				return table;

			throw new InvalidOperationException (
				"No MetaTable has been set on control '" + (control == null ? "(null)" : control.ID) +
				"'. Call SetMetaTable (table) on it, or bind it to a data source implementing " +
				"IDynamicDataSource.");
		}

		/// <summary>Identity key for the Items entry: equal when it wraps the same control INSTANCE.</summary>
		/// <remarks>
		/// A bare control reference would work as a key, but Items is shared across the whole request, so
		/// it could collide with an entry a module or the page put there under the same control.
		/// Wrapping makes the key unambiguous while keeping reference identity - two grids over the same
		/// table are two entries.
		/// </remarks>
		sealed class MetaTableKey
		{
			readonly Control control;

			public MetaTableKey (Control control)
			{
				this.control = control;
			}

			public override bool Equals (object obj)
			{
				var other = obj as MetaTableKey;
				return other != null && ReferenceEquals (other.control, control);
			}

			public override int GetHashCode ()
			{
				return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode (control) ^ 0x4d544b;
			}
		}

		static string GetDataSourceId (IDynamicDataSource dataSource)
		{
			Control c = dataSource as Control;
			if (c == null)
				return String.Empty;
			
			return c.ID;
		}
		
		public static MetaTable GetTable (this IDynamicDataSource dataSource)
		{
			if (dataSource == null)
				return null;

			string entitySetName = dataSource.EntitySetName;
			if (String.IsNullOrEmpty (entitySetName)) {
				// LAMESPEC: MSDN says we should throw in this case, but .NET calls
				// DynamicDataRouteHandler.GetRequestMetaTable(HttpContext
				// httpContext) instead (eventually)
				MetaTable ret = DynamicDataRouteHandler.GetRequestMetaTable (HttpContext.Current);
				if (ret == null)
					throw new InvalidOperationException ("The control '" + GetDataSourceId (dataSource) +
									     "' does not have a TableName property and a table name cannot be inferred from the URL.");
			}
			
			Type contextType = dataSource.ContextType;
			if (contextType == null)
				throw new InvalidOperationException ("The ContextType property of control '" + GetDataSourceId (dataSource) + "' must specify a data context");
			
			return MetaModel.GetModel (contextType).GetTable (entitySetName);
		}

		[MonoTODO]
		public static void LoadWithForeignKeys (this LinqDataSource dataSource, Type rowType)
		{
			throw new NotImplementedException ();
		}
	}
}
