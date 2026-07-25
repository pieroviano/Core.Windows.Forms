' Compiled AT RUNTIME by BuildManager into its own assembly, using the site's defaultLanguage (vb).
Imports System

Namespace WebFormsSampleVB.AppCode
    Public Module Helper
        Public Function Shout(ByVal s As String) As String
            Return If(s, String.Empty).ToUpperInvariant() & "!"
        End Function
    End Module
End Namespace
