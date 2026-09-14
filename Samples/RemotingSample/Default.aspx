<%@ Page Language="C#" AutoEventWireup="true" EnableSessionState="false" %>
<% Response.AppendHeader ("X-Application", System.Web.HttpRuntime.AppDomainAppVirtualPath); %>
<!DOCTYPE html>
<html>
<head><title>Remoting sample</title></head>
<body>
    <!-- Session-free, so it renders wherever the application runs - including a child domain, where
         the host has not necessarily configured a state server. -->
    <p>pid: <%= System.Environment.ProcessId %></p>
    <p>vpath: <%= System.Web.HttpRuntime.AppDomainAppVirtualPath %></p>
    <p>method: <%= Request.HttpMethod %></p>
    <p>echo: <%= Server.HtmlEncode (Request ["x"]) %></p>
</body>
</html>
