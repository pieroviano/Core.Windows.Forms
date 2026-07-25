Imports System.Web

Namespace WebFormsSampleVB
    ''' <summary>
    ''' The classic Global.asax code-behind shape, in VB. Global.asax here keeps its handlers inline in
    ''' a &lt;script runat="server"&gt; block and declares no Inherits=, so the runtime compiles and uses
    ''' that one; this class exercises the .asax nesting in the project tree.
    ''' </summary>
    Public Class GlobalApplication
        Inherits HttpApplication
    End Class
End Namespace
