<%@ Page Language="C#" Inherits="WebFormsSample.EventLogPage" AutoEventWireup="true" %>
<%@ Import Namespace="System.Collections.Generic" %>
<%@ Import Namespace="System.Linq" %>
<%--
  Command events from data-bound controls.

  GridView (GridView.HandleEvent) raises RowCommand FIRST for every command - including the pager and the
  sort headers, which post back through the grid itself - and then the specific event. Because the grid
  is bound manually rather than through DataSourceID, the page must handle PageIndexChanging, Sorting,
  RowEditing, RowCancelingEdit, RowUpdating and RowDeleting itself, and GridViewSortEventArgs.SortDirection
  is always Ascending: the grid only remembers the sort when a data source control does the sorting.

  Repeater and DataList receive their commands by bubbling: the LinkButton raises Command, the item
  wraps it, and the list raises ItemCommand (DataList then also handles "Select").

  The rows live in view state as a plain string, so the page needs no session and no serializer.
--%>
<script runat="server">
    [Serializable]
    public class Item { public int Id { get; set; } public string Name { get; set; } }

    List<Item> Items {
        get {
            string raw = (string) ViewState ["items"] ?? "1:alpha;2:bravo;3:charlie;4:delta;5:echo";
            return raw.Split (';').Select (p => p.Split (':'))
                      .Select (p => new Item { Id = Int32.Parse (p [0]), Name = p [1] }).ToList ();
        }
        set { ViewState ["items"] = String.Join (";", value.Select (i => i.Id + ":" + i.Name)); }
    }

    string SortExpression {
        get { return (string) ViewState ["sort"] ?? "Id"; }
        set { ViewState ["sort"] = value; }
    }

    void Page_Load (object sender, EventArgs e)
    {
        if (!IsPostBack) {
            BindGrid ();
            rep.DataSource = Items; rep.DataBind ();
            dl.DataSource = Items; dl.DataBind ();
        }
    }

    void BindGrid ()
    {
        IEnumerable<Item> rows = Items;
        rows = SortExpression == "Name" ? rows.OrderBy (i => i.Name) : rows.OrderBy (i => i.Id);
        grid.DataSource = rows.ToList ();
        grid.DataBind ();
    }

    protected void GridRowCommand (object sender, GridViewCommandEventArgs e)
    {
        Log ("grid.RowCommand(" + e.CommandName + "," + e.CommandArgument + ")");

        if (e.CommandName == "Bump") {
            int key = (int) grid.DataKeys [Int32.Parse ((string) e.CommandArgument)].Value;
            List<Item> items = Items;
            items.First (i => i.Id == key).Name += "+";
            Items = items;
            BindGrid ();
        }
    }

    protected void GridPageIndexChanging (object sender, GridViewPageEventArgs e)
    {
        Log ("grid.PageIndexChanging(" + e.NewPageIndex + ")");
        grid.PageIndex = e.NewPageIndex;
        BindGrid ();
    }

    protected void GridPageIndexChanged (object sender, EventArgs e) { Log ("grid.PageIndexChanged(" + grid.PageIndex + ")"); }

    protected void GridSorting (object sender, GridViewSortEventArgs e)
    {
        Log ("grid.Sorting(" + e.SortExpression + "," + e.SortDirection + ")");
        SortExpression = e.SortExpression;
        BindGrid ();
    }

    protected void GridSorted (object sender, EventArgs e) { Log ("grid.Sorted"); }

    protected void GridSelectedIndexChanging (object sender, GridViewSelectEventArgs e)
    {
        Log ("grid.SelectedIndexChanging(" + e.NewSelectedIndex + ")");
    }

    protected void GridSelectedIndexChanged (object sender, EventArgs e)
    {
        Log ("grid.SelectedIndexChanged(" + grid.SelectedIndex + ",key=" + grid.SelectedDataKey.Value + ")");
    }

    protected void GridRowEditing (object sender, GridViewEditEventArgs e)
    {
        Log ("grid.RowEditing(" + e.NewEditIndex + ")");
        grid.EditIndex = e.NewEditIndex;
        BindGrid ();
    }

    protected void GridRowCancelingEdit (object sender, GridViewCancelEditEventArgs e)
    {
        Log ("grid.RowCancelingEdit(" + e.RowIndex + ")");
        grid.EditIndex = -1;
        BindGrid ();
    }

    protected void GridRowUpdating (object sender, GridViewUpdateEventArgs e)
    {
        Log ("grid.RowUpdating(" + e.RowIndex + ",key=" + e.Keys ["Id"] + ",Name=" + e.NewValues ["Name"] + ")");
        int key = (int) e.Keys ["Id"];
        List<Item> items = Items;
        items.First (i => i.Id == key).Name = (string) e.NewValues ["Name"];
        Items = items;
        grid.EditIndex = -1;
        BindGrid ();
    }

    protected void GridRowDeleting (object sender, GridViewDeleteEventArgs e)
    {
        Log ("grid.RowDeleting(" + e.RowIndex + ",key=" + e.Keys ["Id"] + ")");
        int key = (int) e.Keys ["Id"];
        Items = Items.Where (i => i.Id != key).ToList ();
        BindGrid ();
    }

    protected void RepItemCommand (object source, RepeaterCommandEventArgs e)
    {
        Log ("rep.ItemCommand(" + e.CommandName + "," + e.CommandArgument + ",item=" + e.Item.ItemIndex + ")");
    }

    protected void DlItemCommand (object source, DataListCommandEventArgs e)
    {
        Log ("dl.ItemCommand(" + e.CommandName + ",item=" + e.Item.ItemIndex + ")");
    }

    protected void DlSelectedIndexChanged (object sender, EventArgs e)
    {
        Log ("dl.SelectedIndexChanged(" + dl.SelectedIndex + ",key=" + dl.DataKeys [dl.SelectedIndex] + ")");
        dl.DataSource = Items; dl.DataBind ();
    }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>Data control events</title></head>
<body>
  <h1>Data control events</h1>
  <form id="form1" runat="server">
    <h2>GridView</h2>
    <asp:GridView ID="grid" runat="server" AutoGenerateColumns="false" DataKeyNames="Id"
                  AllowPaging="true" PageSize="3" AllowSorting="true"
                  OnRowCommand="GridRowCommand"
                  OnPageIndexChanging="GridPageIndexChanging" OnPageIndexChanged="GridPageIndexChanged"
                  OnSorting="GridSorting" OnSorted="GridSorted"
                  OnSelectedIndexChanging="GridSelectedIndexChanging" OnSelectedIndexChanged="GridSelectedIndexChanged"
                  OnRowEditing="GridRowEditing" OnRowCancelingEdit="GridRowCancelingEdit"
                  OnRowUpdating="GridRowUpdating" OnRowDeleting="GridRowDeleting">
      <SelectedRowStyle CssClass="selected" />
      <Columns>
        <asp:BoundField DataField="Id" HeaderText="Id" ReadOnly="true" SortExpression="Id" />
        <asp:BoundField DataField="Name" HeaderText="Name" SortExpression="Name" />
        <asp:CommandField ShowSelectButton="true" ShowEditButton="true" ShowDeleteButton="true" />
        <asp:ButtonField CommandName="Bump" Text="Bump" ButtonType="Button" />
      </Columns>
    </asp:GridView>

    <h2>Repeater</h2>
    <asp:Repeater ID="rep" runat="server" OnItemCommand="RepItemCommand">
      <ItemTemplate>
        <asp:LinkButton ID="pick" runat="server" CommandName="Pick" CommandArgument='<%# Eval ("Id") %>'
                        Text='<%# Eval ("Name") %>' />
      </ItemTemplate>
    </asp:Repeater>

    <h2>DataList</h2>
    <asp:DataList ID="dl" runat="server" DataKeyField="Id" RepeatLayout="Flow"
                  OnItemCommand="DlItemCommand" OnSelectedIndexChanged="DlSelectedIndexChanged">
      <ItemTemplate>
        <asp:LinkButton ID="choose" runat="server" CommandName="Select" Text='<%# Eval ("Name") %>' />
      </ItemTemplate>
      <SelectedItemTemplate><span class="chosen">[<%# Eval ("Name") %>]</span></SelectedItemTemplate>
    </asp:DataList>

    <h2>Events</h2>
    <p>Postbacks: <asp:Label ID="postbacks" runat="server" /></p>
    <p><asp:Label ID="log" runat="server" /></p>
  </form>
</body>
</html>
