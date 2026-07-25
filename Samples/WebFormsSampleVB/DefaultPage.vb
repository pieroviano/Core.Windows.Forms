Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Web.UI
Imports System.Web.UI.WebControls

Namespace WebFormsSampleVB
    ''' <summary>
    ''' Code-behind for Default.aspx. The .aspx names this class with Inherits=, and the generated
    ''' page class derives from it and assigns the runat="server" controls to these Protected fields,
    ''' so their names must match the control IDs - exactly as in C#.
    ''' </summary>
    Public Class DefaultPage
        Inherits Page

        Protected message As Label
        Protected who As TextBox
        Protected whoRequired As RequiredFieldValidator
        Protected greet As Button
        Protected greeting As Label
        Protected counter As Label
        ' Named itemList, not items: VB is case-insensitive, so a control called "items" collides
        ' with Page.Items and needs an explicit Shadows. Worth knowing when migrating a real VB site -
        ' the generated partial class hits the same rule against every Page/Control member.
        Protected itemList As Repeater
        Protected grid As GridView

        ' Survives postbacks through __VIEWSTATE rather than a field.
        Private Property PostbackCount As Integer
            Get
                Dim v As Object = ViewState("postbacks")
                Return If(v Is Nothing, 0, CInt(v))
            End Get
            Set(value As Integer)
                ViewState("postbacks") = value
            End Set
        End Property

        Protected Overrides Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)

            message.Text = If(IsPostBack, "hello again (postback)", "hello from OnLoad")

            If IsPostBack Then
                PostbackCount = PostbackCount + 1
            End If
            counter.Text = PostbackCount.ToString()

            If Not IsPostBack Then
                itemList.DataSource = New List(Of String) From {"alpha", "beta", "gamma"}
                itemList.DataBind()

                grid.DataSource = BuildTable()
                grid.DataBind()
            End If
        End Sub

        Private Shared Function BuildTable() As DataTable
            Dim t As New DataTable("widgets")
            t.Columns.Add("Id", GetType(Integer))
            t.Columns.Add("Name", GetType(String))
            t.Columns.Add("Price", GetType(Decimal))
            t.Rows.Add(1, "sprocket", 9.99D)
            t.Rows.Add(2, "flange", 24.5D)
            t.Rows.Add(3, "grommet", 3.75D)
            Return t
        End Function

        ' Wired from the .aspx via OnClick="OnGreet".
        Protected Sub OnGreet(sender As Object, e As EventArgs)
            If Not Page.IsValid Then
                greeting.Text = "(validation failed: " & whoRequired.ErrorMessage & ")"
                Return
            End If

            greeting.Text = If(String.IsNullOrEmpty(who.Text),
                               "(nothing typed)",
                               "Hello, " & Server.HtmlEncode(who.Text) & "!")
        End Sub
    End Class
End Namespace
