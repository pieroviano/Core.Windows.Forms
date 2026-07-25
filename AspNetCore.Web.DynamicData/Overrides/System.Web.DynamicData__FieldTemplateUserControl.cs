//
// FieldTemplateUserControl.cs
//
// Author:
//	Atsushi Enomoto <atsushi@ximian.com>
//	Marek Habersack <mhabersack@novell.com>
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
// Upstream declares the four members a field template actually binds to - FieldValue,
// FieldValueString, FieldValueEditString and DataControl - as [MonoTODO] auto-properties with a
// private setter, and nothing anywhere assigns them. Every accessor that would compute one
// (GetColumnValue, FormatFieldValue, ConvertEditedValue, ExtractValues) throws
// NotImplementedException. The consequence is that a FieldTemplates/Text.ascx binding
// <%# FieldValueString %> renders empty and an edit template discards whatever the user typed,
// silently, on a page that returns HTTP 200.
//
// That is not a small patch. The values have to come from somewhere, and "somewhere" is the host:
// DynamicControl implements IFieldTemplateHost, resolves the MetaColumn and the formatting options,
// and hosts this control. All of that already works - the plumbing arrives at this class and stops.
// So the whole of this file's behaviour is written here, and the members that genuinely have no
// meaning without foreign-key metadata still throw, with their upstream [MonoTODO] intact.
//
// Deliberately NOT reimplemented, and still throwing: BuildChildrenPath, BuildForeignKeyPath,
// ExtractForeignKey, FindOtherFieldTemplate, PopulateListControl and SetUpValidator(validator,
// column). Those need association metadata that QueryableDataModelProvider does not produce - it
// reads IQueryable members, not a relational schema with foreign keys - so implementing them would
// mean inventing relationships. Throwing where the data does not exist is better than returning
// something plausible.
//
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Security.Permissions;
using System.Security.Principal;
using System.Web.Caching;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace System.Web.DynamicData
{
	[AspNetHostingPermission (SecurityAction.InheritanceDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	[AspNetHostingPermission (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	public class FieldTemplateUserControl : UserControl, IBindableControl, IFieldTemplate
	{
		object fieldValue;
		bool fieldValueSet;

		public MetaChildrenColumn ChildrenColumn {
			get {
				MetaColumn column = Column;
				var ret = column as MetaChildrenColumn;
				if (ret == null) {
					string name = column == null ? null : column.Name;
					throw new Exception ("'" + name + "' is not a children column and cannot be used here.");
				}

				return ret;
			}
		}

		[MonoTODO]
		protected string ChildrenPath {
			get { return ChildrenColumn.GetChildrenListPath (Row); }

		}

		public MetaColumn Column {
			get {
				IFieldTemplateHost host = Host;
				if (host != null)
					return host.Column;

				return null;
			}
		}

		/// <summary>
		/// The control inside this template that carries the value - the one an edit template posts
		/// back and ExtractValues reads.
		/// </summary>
		/// <remarks>
		/// Found by walking the control tree rather than declared, because a field template is an .ascx
		/// written by the application: it can name its TextBox anything, wrap it in a panel, or put a
		/// validator beside it.
		///
		/// Only EDITABLE controls count, and that is the whole subtlety. A read-only template like
		/// `&lt;%# FieldValueString %&gt;` compiles to a DataBoundLiteralControl, which implements
		/// ITextControl - so accepting ITextControl finds the rendered display string and hands it back
		/// as if the user had typed it. That is how a read-only int column came to be "extracted" as an
		/// empty string and then thrown on by Int32's converter. A literal is output, not input.
		///
		/// The order matters for the same reason: a CheckBox and a ListControl both also carry text.
		/// </remarks>
		public virtual Control DataControl {
			get {
				return FindDataControl (this, typeof (ICheckBoxControl))
				       ?? FindDataControl (this, typeof (ListControl))
				       ?? FindDataControl (this, typeof (IEditableTextControl));
			}
		}

		static Control FindDataControl (Control parent, Type wanted)
		{
			foreach (Control child in parent.Controls) {
				if (wanted.IsInstanceOfType (child))
					return child;

				// A validator is not a value, and neither is a literal the template wrote around one.
				if (child is BaseValidator)
					continue;

				Control found = FindDataControl (child, wanted);
				if (found != null)
					return found;
			}

			return null;
		}

		/// <summary>The raw value of this template's column on the current row.</summary>
		public virtual object FieldValue {
			get {
				// Cached per instantiation rather than per get: Row goes through Page.GetDataItem (),
				// which is only valid during data binding, and a template that reads FieldValue from
				// Render would otherwise get null instead of the value it displayed.
				if (!fieldValueSet) {
					fieldValue = GetColumnValue (Column);
					fieldValueSet = true;
				}

				return fieldValue;
			}

			set {
				fieldValue = value;
				fieldValueSet = true;
			}
		}

		/// <summary>The value as an edit control should show it - unformatted unless asked otherwise.</summary>
		public virtual string FieldValueEditString {
			get {
				object value = FieldValue;
				IFieldFormattingOptions options = FormattingOptions;

				// DataFormatString is a DISPLAY format. Applying it in edit mode turns "49.95" into
				// "$49.95" and the next postback fails to parse it, so it is opt-in through
				// ApplyFormatInEditMode - which is exactly why that property exists.
				if (options != null && options.ApplyFormatInEditMode)
					return FormatFieldValue (value);

				return value == null ? String.Empty : value.ToString ();
			}
		}

		/// <summary>The value as a read-only template should show it: formatted, and HTML-encoded.</summary>
		public virtual string FieldValueString {
			get { return FormatFieldValue (FieldValue); }
		}

		[MonoTODO]
		public MetaForeignKeyColumn ForeignKeyColumn {
			get {
				MetaColumn column = Column;
				var ret = column as MetaForeignKeyColumn;
				if (ret == null) {
					string name = column == null ? null : column.Name;
					throw new Exception ("'" + name + "' is not a foreign key column and cannot be used here.");
				}

				return ret;
			}
		}

		[MonoTODO]
		protected string ForeignKeyPath {
			get { return ForeignKeyColumn.GetForeignKeyDetailsPath (Row); }
		}

		public IFieldFormattingOptions FormattingOptions {
			get {
				IFieldTemplateHost host = Host;
				return host == null ? null : host.FormattingOptions;
			}
		}

		public IFieldTemplateHost Host { get; private set; }

		public System.ComponentModel.AttributeCollection MetadataAttributes {
			get {
				MetaColumn column = Column;
				if (column == null)
					return null;

				return column.Attributes;
			}
		}

		public DataBoundControlMode Mode {
			get {
				IFieldTemplateHost host = Host;
				return host == null ? DataBoundControlMode.ReadOnly : host.Mode;
			}
		}

		public virtual object Row {
			get {
				Page page = Page;
				return page == null ? null : page.GetDataItem ();
			}
		}

		public MetaTable Table {
			get {
				MetaColumn column = Column;
				return column == null ? null : column.Table;
			}
		}

		[MonoTODO]
		protected string BuildChildrenPath (string path)
		{
			throw new NotImplementedException ();
		}

		[MonoTODO]
		protected string BuildForeignKeyPath (string path)
		{
			throw new NotImplementedException ();
		}

		/// <summary>Turns what an edit control posted back into a value of the column's type.</summary>
		protected virtual object ConvertEditedValue (string value)
		{
			MetaColumn column = Column;
			IFieldFormattingOptions options = FormattingOptions;

			if (column == null)
				return value;

			Type target = Nullable.GetUnderlyingType (column.ColumnType) ?? column.ColumnType;

			if (String.IsNullOrEmpty (value)) {
				// An empty text box means "no value" for a nullable column and "" for a string one, and
				// the difference matters to the store. ConvertEmptyStringToNull is how the column says
				// which it wants - but it only gets a say for a string: there is no number, date or Guid
				// that an empty box could mean, and handing "" to Int32's converter throws.
				if (value == null || target != typeof (string))
					return null;

				return (options == null || options.ConvertEmptyStringToNull) ? null : value;
			}

			if (target == typeof (string))
				return value;

			// The column's own TypeConverter first: an application can attach one, and it knows the
			// culture-specific round-trip better than Convert does.
			TypeConverter converter = TypeDescriptor.GetConverter (target);
			if (converter != null && converter.CanConvertFrom (typeof (string)))
				return converter.ConvertFromString (null, CultureInfo.CurrentCulture, value);

			return Convert.ChangeType (value, target, CultureInfo.CurrentCulture);
		}

		[MonoTODO]
		protected virtual void ExtractForeignKey (IDictionary dictionary, string selectedValue)
		{
			throw new NotImplementedException ();
		}

		/// <summary>Writes this template's edited value into the dictionary the data source will apply.</summary>
		protected virtual void ExtractValues (IOrderedDictionary dictionary)
		{
			if (dictionary == null)
				throw new ArgumentNullException ("dictionary");

			MetaColumn column = Column;
			if (column == null)
				return;

			// A read-only template has nothing to contribute, and writing the DISPLAY string into the
			// update dictionary would round-trip a formatted value back into the store - "$49.95" into a
			// decimal column.
			if (Mode == DataBoundControlMode.ReadOnly)
				return;

			Control control = DataControl;
			if (control == null)
				return;

			object value;

			if (control is ICheckBoxControl check)
				value = check.Checked;
			else if (control is ListControl list)
				value = ConvertEditedValue (list.SelectedValue);
			else if (control is ITextControl text)
				value = ConvertEditedValue (text.Text);
			else
				return;

			dictionary [column.Name] = value;
		}

		[MonoTODO]
		protected FieldTemplateUserControl FindOtherFieldTemplate (string columnName)
		{
			throw new NotImplementedException ();
		}

		/// <summary>Applies DataFormatString, NullDisplayText and HtmlEncode, in that order.</summary>
		public virtual string FormatFieldValue (object fieldValue)
		{
			IFieldFormattingOptions options = FormattingOptions;

			string format = options == null ? null : options.DataFormatString;
			string nullText = options == null ? String.Empty : options.NullDisplayText;
			bool encode = options == null ? true : options.HtmlEncode;

			if (fieldValue == null)
				// NullDisplayText is the application's own markup when it wants an em-dash or an icon, so
				// it is never encoded - the same rule BoundField follows.
				return nullText ?? String.Empty;

			string formatted = String.IsNullOrEmpty (format)
					   ? Convert.ToString (fieldValue, CultureInfo.CurrentCulture)
					   : String.Format (CultureInfo.CurrentCulture, format, fieldValue);

			if (formatted == null)
				return String.Empty;

			// Encode AFTER formatting, so a format string cannot be used to smuggle markup through, and
			// so the encoding covers the value rather than the template's own punctuation.
			return encode ? HttpUtility.HtmlEncode (formatted) : formatted;
		}

		/// <summary>Reads a column off the current row.</summary>
		protected virtual object GetColumnValue (MetaColumn column)
		{
			if (column == null)
				return null;

			object row = Row;
			if (row == null)
				return null;

			// EntityTypeProperty is the PropertyInfo the model provider resolved, so this does not
			// re-reflect per row and does not depend on the row being the declared entity type - a
			// projection ("new (Id, Name)") produces an anonymous type, and the fallback covers it.
			PropertyInfo property = column.EntityTypeProperty;
			if (property != null && property.DeclaringType != null &&
			    property.DeclaringType.IsInstanceOfType (row))
				return property.GetValue (row, null);

			PropertyDescriptor descriptor = TypeDescriptor.GetProperties (row) [column.Name];
			return descriptor == null ? null : descriptor.GetValue (row);
		}

		void IBindableControl.ExtractValues (IOrderedDictionary dictionary)
		{
			ExtractValues (dictionary);
		}

		void IFieldTemplate.SetHost (IFieldTemplateHost host)
		{
			Host = host;
		}

		[MonoTODO]
		protected void IgnoreModelValidationAttribute (Type attributeType)
		{
			throw new NotImplementedException ();
		}

		[MonoTODO]
		protected void PopulateListControl (ListControl listControl)
		{
			throw new NotImplementedException ();
		}

		/// <summary>Points a validator at this template's data control and validation group.</summary>
		protected virtual void SetUpValidator (BaseValidator validator)
		{
			if (validator == null)
				return;

			Control control = DataControl;
			if (control != null && !String.IsNullOrEmpty (control.ID))
				validator.ControlToValidate = control.ID;

			IFieldTemplateHost host = Host;
			if (host != null && !String.IsNullOrEmpty (host.ValidationGroup))
				validator.ValidationGroup = host.ValidationGroup;

			MetaColumn column = Column;
			if (column != null)
				validator.ErrorMessage = column.RequiredErrorMessage;
		}

		[MonoTODO]
		protected virtual void SetUpValidator (BaseValidator validator, MetaColumn column)
		{
			throw new NotImplementedException ();
		}
	}
}
