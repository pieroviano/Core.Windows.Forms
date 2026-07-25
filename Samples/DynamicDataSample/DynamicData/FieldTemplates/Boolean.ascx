<%@ Control Language="C#" Inherits="System.Web.DynamicData.FieldTemplateUserControl" %>
<%--
  Booleans get their own template because a checkbox reads better than "True"/"False" - which is the
  whole argument for field templates: the model says "this column is a bool", and how a bool looks is
  decided once, here, rather than at every call site.
--%>
<asp:CheckBox ID="CheckBox1" runat="server" Enabled="false" Checked='<%# (bool) (FieldValue ?? false) %>' />
