<%@ Page Language="C#" Inherits="WebFormsSample.EventLogPage" AutoEventWireup="true" %>
<%--
  AutoPostBack: the control submits the form itself, through __doPostBack, the moment its value
  changes - no button is involved, so the only server event raised is the control's changed event.

  Rules worth watching (all from the controls' LoadPostData):
    - a CheckBox that is UNchecked posts nothing at all, yet still raises CheckedChanged - it registers
      itself with Page.RegisterRequiresPostBack during PreRender so LoadPostData runs regardless;
    - a RadioButton raises CheckedChanged only for the button being checked, never for the one in the
      same group that became unchecked;
    - list controls raise SelectedIndexChanged once per postback, however many items changed.
--%>
<script runat="server">
    string Selected (ListControl list)
    {
        var values = new System.Collections.Generic.List<string> ();
        foreach (ListItem item in list.Items)
            if (item.Selected)
                values.Add (item.Value);
        return String.Join (",", values);
    }

    protected void NameChanged (object sender, EventArgs e)     { Log ("name.TextChanged(" + name.Text + ")"); }
    protected void SizeChanged (object sender, EventArgs e)     { Log ("size.SelectedIndexChanged(" + size.SelectedValue + ")"); }
    protected void AgreeChanged (object sender, EventArgs e)    { Log ("agree.CheckedChanged(" + agree.Checked + ")"); }
    protected void ShipChanged (object sender, EventArgs e)     { Log ("ship.CheckedChanged(" + ship.Checked + ")"); }
    protected void PickupChanged (object sender, EventArgs e)   { Log ("pickup.CheckedChanged(" + pickup.Checked + ")"); }
    protected void ToppingsChanged (object sender, EventArgs e) { Log ("toppings.SelectedIndexChanged(" + Selected (toppings) + ")"); }
    protected void PayChanged (object sender, EventArgs e)      { Log ("pay.SelectedIndexChanged(" + pay.SelectedValue + ")"); }
    protected void TagsChanged (object sender, EventArgs e)     { Log ("tags.SelectedIndexChanged(" + Selected (tags) + ")"); }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>AutoPostBack</title></head>
<body>
  <h1>AutoPostBack</h1>
  <form id="form1" runat="server">
    <p><label for="name">Name</label>
      <asp:TextBox ID="name" runat="server" AutoPostBack="true" OnTextChanged="NameChanged" /></p>

    <p><label for="size">Size</label>
      <asp:DropDownList ID="size" runat="server" AutoPostBack="true" OnSelectedIndexChanged="SizeChanged">
        <asp:ListItem Value="S" Text="Small" />
        <asp:ListItem Value="M" Text="Medium" />
        <asp:ListItem Value="L" Text="Large" />
      </asp:DropDownList></p>

    <p><asp:CheckBox ID="agree" runat="server" Text="I agree" AutoPostBack="true" OnCheckedChanged="AgreeChanged" /></p>

    <p>
      <asp:RadioButton ID="ship" runat="server" GroupName="delivery" Text="Ship" Checked="true"
                       AutoPostBack="true" OnCheckedChanged="ShipChanged" />
      <asp:RadioButton ID="pickup" runat="server" GroupName="delivery" Text="Pick up"
                       AutoPostBack="true" OnCheckedChanged="PickupChanged" />
    </p>

    <asp:CheckBoxList ID="toppings" runat="server" AutoPostBack="true" OnSelectedIndexChanged="ToppingsChanged"
                      RepeatLayout="Flow">
      <asp:ListItem Value="cheese" Text="Cheese" />
      <asp:ListItem Value="ham" Text="Ham" />
      <asp:ListItem Value="olives" Text="Olives" />
    </asp:CheckBoxList>

    <asp:RadioButtonList ID="pay" runat="server" AutoPostBack="true" OnSelectedIndexChanged="PayChanged"
                         RepeatLayout="Flow">
      <asp:ListItem Value="card" Text="Card" />
      <asp:ListItem Value="cash" Text="Cash" />
    </asp:RadioButtonList>

    <p><asp:ListBox ID="tags" runat="server" SelectionMode="Multiple" AutoPostBack="true"
                    OnSelectedIndexChanged="TagsChanged" Rows="3">
        <asp:ListItem Value="red" Text="Red" />
        <asp:ListItem Value="green" Text="Green" />
        <asp:ListItem Value="blue" Text="Blue" />
      </asp:ListBox></p>

    <h2>Events</h2>
    <p>Postbacks: <asp:Label ID="postbacks" runat="server" /></p>
    <p><asp:Label ID="log" runat="server" /></p>
  </form>
</body>
</html>
