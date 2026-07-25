<%@ Control Language="VB" ClassName="WidgetBoxVB" %>
<script runat="server">
    Public Property Caption As String
</script>
<div class="widget-vb">
  <strong><%= Server.HtmlEncode(If(Caption, "(no caption)")) %></strong>
</div>
