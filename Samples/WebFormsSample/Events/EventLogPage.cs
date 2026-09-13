using System;
using System.Collections.Generic;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace WebFormsSample
{
	// Base class for the pages under Events/. Each of them demonstrates a group of server-side events,
	// and a browser test asserts on the exact sequence the page raised - so the recording has to be
	// correct on ASP.NET itself, independently of anything the port does.
	//
	// Two records are kept:
	//
	//   Note (...) - this request only, rendered into a control with ID "trace". Used for the lifecycle,
	//                where the question is "in what order did THIS request run".
	//   Log (...)  - accumulated across postbacks through view state, rendered into a control with ID
	//                "log". Used for control events, where a test performs several postbacks.
	//
	// Why the log is not simply ViewState ["log"] += entry: entries can be written during Init (a
	// MultiView raises ActiveViewChanged from its OnInit on the first request), which is before the page
	// tracks view state. A value written then is not dirty and is silently not saved. So entries are
	// collected in a field and written to view state exactly once, in PreRenderComplete - after every
	// event, and before SaveStateComplete. The previous postbacks' log is read back at the same point,
	// by which time LoadAllState has long since run.
	public class EventLogPage : Page
	{
		const string LogKey = "EventLogPage.log";

		readonly List<string> notes = new List<string> ();
		readonly List<string> entries = new List<string> ();
		string log;

		/// <summary>Records an entry for this request only.</summary>
		public void Note (string entry)
		{
			notes.Add (entry);
		}

		/// <summary>Records an entry in the log that accumulates across postbacks.</summary>
		public void Log (string entry)
		{
			entries.Add (entry);
		}

		/// <summary>Number of postbacks this page instance has seen, carried in view state.</summary>
		protected int Postbacks {
			get { object v = ViewState ["EventLogPage.postbacks"]; return v == null ? 0 : (int) v; }
			set { ViewState ["EventLogPage.postbacks"] = value; }
		}

		protected override void OnLoad (EventArgs e)
		{
			if (IsPostBack)
				Postbacks = Postbacks + 1;

			base.OnLoad (e);
		}

		protected override void OnPreRenderComplete (EventArgs e)
		{
			base.OnPreRenderComplete (e);

			string previous = (string) ViewState [LogKey] ?? String.Empty;
			string current = String.Join (" | ", entries);

			log = previous.Length == 0 ? current : current.Length == 0 ? previous : previous + " | " + current;
			ViewState [LogKey] = log;
		}

		protected override void Render (HtmlTextWriter writer)
		{
			// Render, not PreRender: the trace must include PreRenderComplete and SaveStateComplete. A
			// label's text set after SaveStateComplete is still rendered; it is just not persisted, which
			// is what is wanted - each response shows its own trace.
			SetText ("trace", String.Join (" | ", notes));
			SetText ("log", String.IsNullOrEmpty (log) ? "(no events yet)" : log);
			SetText ("postbacks", Postbacks.ToString ());

			base.Render (writer);
		}

		void SetText (string id, string text)
		{
			Control target = FindRecursive (this, id);

			if (target is Label)
				((Label) target).Text = text;
			else if (target is Literal)
				((Literal) target).Text = text;
		}

		// Page.FindControl only searches the page's own naming container; with a master page the
		// labels live inside a ContentPlaceHolder, several naming containers down.
		static Control FindRecursive (Control root, string id)
		{
			foreach (Control child in root.Controls) {
				if (child.ID == id)
					return child;

				Control found = FindRecursive (child, id);
				if (found != null)
					return found;
			}

			return null;
		}
	}
}
