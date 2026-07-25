using System;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace WebFormsSample
{
	// Code-behind for Events.aspx. Demonstrates the three server-side events a simple form raises, and
	// the order the page lifecycle raises them in.
	//
	// Neither input sets AutoPostBack, so nothing posts back until the button is pressed - and then a
	// SINGLE postback raises all three events. That is deliberate: AutoPostBack only controls whether
	// the control triggers a postback itself, it has nothing to do with whether the changed event
	// fires. LoadPostData compares the posted value with the one restored from view state, and any
	// control whose value moved gets RaisePostDataChangedEvent during RaiseChangedEvents - which the
	// page runs BEFORE the postback (click) event.
	public class EventsPage : Page
	{
		protected TextBox who;
		protected DropDownList colour;
		protected Button go;
		protected Label log;
		protected Label count;

		// Accumulates across postbacks, so pressing Submit repeatedly appends rather than replaces.
		//
		// Held as a single string rather than a List<string>: strings have a native encoding in
		// ObjectStateFormatter, so this works whether or not the host installed an
		// IStateObjectSerializer. A collection would depend on one - see IStateObjectSerializer.
		string Log {
			get { return (string) (ViewState ["log"] ?? String.Empty); }
			set { ViewState ["log"] = value; }
		}

		int Count {
			get { object v = ViewState ["count"]; return v == null ? 0 : (int) v; }
			set { ViewState ["count"] = value; }
		}

		void Append (string detail)
		{
			Count = Count + 1;
			Log = Log.Length == 0 ? detail : Log + " | " + detail;
		}

		// Raised during RaiseChangedEvents, before the click.
		protected void OnWhoTextChanged (object sender, EventArgs e)
		{
			Append ("TextChanged(who=" + who.Text + ")");
		}

		// Also during RaiseChangedEvents. Which of the two changed events comes first is determined by
		// the order the controls appear in the page, not by anything here.
		protected void OnColourSelectedIndexChanged (object sender, EventArgs e)
		{
			Append ("SelectedIndexChanged(colour=" + colour.SelectedValue + ")");
		}

		// The postback event, raised after every changed event has been handled.
		protected void OnGoClick (object sender, EventArgs e)
		{
			Append ("Click(go)");
		}

		// PreRender, not Load: Load runs before any of the handlers above, so writing the label there
		// would always render the PREVIOUS postback's events.
		protected override void OnPreRender (EventArgs e)
		{
			base.OnPreRender (e);

			log.Text = Log.Length == 0 ? "(no events yet)" : Log;
			count.Text = Count.ToString ();
		}
	}
}
