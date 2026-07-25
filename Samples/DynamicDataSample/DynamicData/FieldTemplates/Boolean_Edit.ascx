<%@ Control Language="C#" Inherits="System.Web.DynamicData.FieldTemplateUserControl" %>
<%--
  The same checkbox, enabled. ExtractValues recognises ICheckBoxControl and takes Checked directly
  rather than going through ConvertEditedValue - a checkbox has no string form to parse.
--%>
<asp:CheckBox ID="CheckBox1" runat="server" Checked='<%# (bool) (FieldValue ?? false) %>' />
