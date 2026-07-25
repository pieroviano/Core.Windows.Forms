<%@ Control Language="C#" Inherits="System.Web.DynamicData.FieldTemplateUserControl" %>
<%--
  The edit template. FieldValueEditString rather than FieldValueString: a display format would put a
  currency symbol into the box and the next postback could not parse it back out.

  The TextBox needs no OnDataBinding and no extraction code. ExtractValues finds it through DataControl
  - which walks this control's tree looking for the value-bearing control - and converts what it holds
  to the column's type, so the same template works for a string, a decimal and a DateTime.
--%>
<asp:TextBox ID="TextBox1" runat="server" Text='<%# FieldValueEditString %>'
             MaxLength='<%# Column.MaxLength > 0 ? Column.MaxLength : 0 %>' />
