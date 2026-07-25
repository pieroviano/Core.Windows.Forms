Imports System.Web.UI

Namespace WebFormsSampleVB
    ''' <summary>
    ''' A VB user-control code-behind + designer pair, so the nesting applied by the package's build/
    ''' props and targets is exercised for .ascx.vb and .ascx.designer.vb as well as the C# forms.
    ''' WidgetBox.ascx declares its members inline and does not use Inherits=, so this class is not
    ''' wired to it - it exists to exercise the project tree, not the runtime.
    ''' </summary>
    Public Partial Class WidgetBoxControl
        Inherits UserControl
    End Class
End Namespace
