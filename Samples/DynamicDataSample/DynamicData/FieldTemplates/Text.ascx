<%@ Control Language="C#" Inherits="System.Web.DynamicData.FieldTemplateUserControl" %>
<%--
  The read-only template for everything that is not a boolean.

  FieldTemplateFactory falls back by type - int, long, decimal, Guid, DateTime and TimeSpan all end up
  at "Text" - so this one control renders five of Product's six scaffolded columns. FieldValueString is
  the column's value with DataFormatString and NullDisplayText applied and HTML encoding done, which is
  why there is no Eval and no encoding here.
--%>
<%# FieldValueString %>
