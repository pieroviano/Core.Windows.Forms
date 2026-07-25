<%@ Page Language="C#" %>
<script runat="server">
    // A plain DTO - exactly the kind of type upstream would have pushed through BinaryFormatter.
    public class Basket
    {
        public string Owner { get; set; }
        public int Items { get; set; }
        public decimal Total { get; set; }
    }

    protected override void OnLoad (EventArgs e)
    {
        base.OnLoad (e);

        // ?reject=1 stores something the installed serializer refuses, to prove the refusal is a
        // clear diagnostic naming the type rather than a silent re-encoding.
        if (Request.QueryString ["reject"] == "1")
            ViewState ["bad"] = new Action (() => { });

        Basket b = ViewState ["basket"] as Basket;
        if (b == null)
            b = new Basket { Owner = "piero", Items = 0, Total = 0m };

        b.Items++;
        b.Total += 4.25m;
        ViewState ["basket"] = b;

        Basket s = Session ["basket"] as Basket;
        if (s == null)
            s = new Basket { Owner = "session", Items = 0, Total = 0m };
        s.Items++;
        Session ["basket"] = s;

        info.Text = String.Format ("viewstate: owner={0} items={1} total={2} | session items={3}",
                                   b.Owner, b.Items, b.Total, s.Items);
    }
</script>
<!DOCTYPE html>
<html><body>
  <form id="f" runat="server">
    <p><asp:Label ID="info" runat="server" /></p>
    <asp:Button ID="go" runat="server" Text="Post back" />
  </form>
</body></html>
