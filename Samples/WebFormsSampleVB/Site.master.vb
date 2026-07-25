Imports System.Web.UI

Namespace WebFormsSampleVB
    ''' <summary>
    ''' A VB master-page code-behind + designer pair, so the nesting applied by the package's build/
    ''' props and targets is exercised for .master.vb and .master.designer.vb. Site.master declares its
    ''' placeholders inline and does not use Inherits=, so this class is not wired to it.
    ''' </summary>
    Public Partial Class SiteMaster
        Inherits MasterPage
    End Class
End Namespace
