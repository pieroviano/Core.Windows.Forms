using System;
using System.Collections.Generic;
using System.Data;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace WebFormsSample
{
	// Code-behind for Default.aspx. The .aspx names this class via Inherits=, and the generated page
	// class derives from it and assigns the runat="server" controls to these protected fields - which
	// is why they must be declared here and must match the control IDs.
	public class DefaultPage : Page
	{
		protected Label message;
		protected Literal stamp;
		protected TextBox who;
		protected Button greet;
		protected Label greeting;
		protected Label counter;
		protected Repeater items;
		protected GridView grid;
		protected RequiredFieldValidator whoRequired;

		// Survives postbacks through __VIEWSTATE rather than a field, which is the point of the test.
		int PostbackCount {
			get {
				object v = ViewState ["postbacks"];
				return v == null ? 0 : (int) v;
			}
			set { ViewState ["postbacks"] = value; }
		}

		protected override void OnLoad (EventArgs e)
		{
			base.OnLoad (e);

			message.Text = IsPostBack ? "hello again (postback)" : "hello from Page_Load";
			stamp.Text = "IsPostBack=" + IsPostBack + ", HttpMethod=" + Request.HttpMethod;

			if (IsPostBack)
				PostbackCount = PostbackCount + 1;

			counter.Text = PostbackCount.ToString ();

			if (!IsPostBack) {
				items.DataSource = new List<string> { "alpha", "beta", "gamma" };
				items.DataBind ();

				// Bound once; the rendered rows have to survive later postbacks through view state.
				grid.DataSource = BuildTable ();
				grid.DataBind ();
			}
		}

		static DataTable BuildTable ()
		{
			DataTable t = new DataTable ("widgets");
			t.Columns.Add ("Id", typeof (int));
			t.Columns.Add ("Name", typeof (string));
			t.Columns.Add ("Price", typeof (decimal));
			t.Rows.Add (1, "sprocket", 9.99m);
			t.Rows.Add (2, "flange", 24.50m);
			t.Rows.Add (3, "grommet", 3.75m);
			return t;
		}

		// Wired from the .aspx via OnClick="OnGreet".
		protected void OnGreet (object sender, EventArgs e)
		{
			// Server-side validation must pass before the click does anything, exactly as on
			// .NET Framework - the client script is an optimisation, not the gate.
			if (!Page.IsValid) {
				greeting.Text = "(validation failed: " + whoRequired.ErrorMessage + ")";
				return;
			}

			greeting.Text = String.IsNullOrEmpty (who.Text)
				? "(nothing typed)"
				: "Hello, " + Server.HtmlEncode (who.Text) + "!";
		}
	}
}
