Imports System.Web.UI

Namespace WebFormsSampleVB
    ''' <summary>
    ''' Exercises the .aspx.vb / .aspx.designer.vb nesting rules. Inline.aspx keeps all of its code in
    ''' the page and does not use Inherits=, so this class is not wired to it.
    ''' </summary>
    Public Partial Class InlinePage
        Inherits Page
    End Class
End Namespace
