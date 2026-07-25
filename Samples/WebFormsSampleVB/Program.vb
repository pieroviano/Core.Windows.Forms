'
' Hosts the VB.NET WebForms application in this directory under Kestrel.
'
' The host itself is language-agnostic - it is the same UseWebForms call the C# sample makes. What
' differs is inside the application: every page is Language="VB", and <compilation
' defaultLanguage="vb"> makes VB the default for pages that do not say.
'
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.Extensions.Hosting
Imports System.Web.Hosting.Kestrel

Namespace WebFormsSampleVB
    Module Program
        Sub Main(args As String())
            Dim builder = WebApplication.CreateBuilder(args)
            Dim app = builder.Build()

            app.UseStaticFiles()

            app.UseWebForms(Sub(options)
                                options.PhysicalPath = app.Environment.ContentRootPath
                                options.VirtualPath = "/"
                                options.SiteName = "WebFormsSampleVB"
                                ' Opt in to a serializer for state objects with no native encoding.
                                options.StateSerializer = New System.Web.JsonStateObjectSerializer()
                            End Sub)

            app.Run()
        End Sub
    End Module
End Namespace
