<%@ Page Language="C#" %>
<!DOCTYPE html>
<html>
<head><title>Simple</title></head>
<body>
  <h1>Simple page</h1>
  <p>Inline expression: <%= 2 + 3 %></p>
  <p>Runtime: <%= System.Environment.Version %></p>
  <p>Request path: <%= Request.Path %></p>
  <%
     for (int i = 1; i <= 3; i++) {
         Response.Write ("<span>item " + i + "</span> ");
     }
  %>
</body>
</html>
