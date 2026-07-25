<%@ Control Language="C#" ClassName="WidgetBox" %>
<script runat="server">
    public string Caption { get; set; }
    public int Count { get; set; }
</script>
<div class="widget">
  <strong><%= Server.HtmlEncode (Caption ?? "(no caption)") %></strong>
  <span> count=<%= Count %></span>
  <span> shouted=<%= WebFormsSample.AppCode.Helper.Shout (Caption) %></span>
</div>
