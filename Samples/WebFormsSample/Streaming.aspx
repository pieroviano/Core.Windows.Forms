<%@ Page Language="C#" %>
<%--
  Proves the response path streams rather than buffering to the end.

  Each chunk is written and flushed with a delay between them. On a runtime that holds everything until
  the pipeline returns, the page still renders correctly - identical bytes, just all at once - which is
  why a test asserting on the finished body cannot tell the difference. A test has to read the response
  as it arrives.
--%>
<%
    Response.ContentType = "text/plain";
    Response.BufferOutput = false;

    for (int i = 0; i < 3; i++) {
        Response.Write ("chunk" + i + "\n");
        Response.Flush ();
        System.Threading.Thread.Sleep (250);
    }
%>
