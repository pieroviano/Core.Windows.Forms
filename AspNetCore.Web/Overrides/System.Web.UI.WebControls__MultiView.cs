//
// System.Web.UI.WebControls.MultiView.cs
//
// Authors:
//	Lluis Sanchez Gual (lluis@novell.com)
//
// (C) 2004 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Copyright (C) 2004 Novell, Inc (http://www.novell.com)
//
//
// PORT OVERRIDE. Upstream's MultiView raised Activate/Deactivate/ActiveViewChanged on EVERY assignment
// to ActiveViewIndex, including the declarative index being re-applied in OnInit on each postback - so a
// page saw "view activated, active view changed" on every request, not just when the view changed. It
// also let every navigation command keep bubbling, and quietly ignored a NextView on the last view and an
// unknown SwitchViewByID.
//
// Rewritten to ASP.NET's semantics (referencesource/System.Web/UI/WebControls/MultiView.cs):
//
//   - an index assigned before the views exist is cached and applied in OnInit;
//   - the events fire only when the index actually changes, and only when "ShouldTriggerViewEvent":
//     on the first request (so the declared view is activated once), or once control state has been
//     applied on a postback. The declarative index re-applied in OnInit on a postback, before control
//     state restores the real one, is therefore silent;
//   - order: Deactivate (old view), Activate (new view), ActiveViewChanged;
//   - handled navigation commands return true, stopping the bubble; NextView on the last view
//     deactivates every view (index -1); SwitchViewByID naming no child view throws;
//   - control state is a Pair (base state, index), as upstream ASP.NET saves it.
//
// Upstream's View has no Active flag; its VisibleInternal (which MultiView alone may set) plays that part.
//

using System;
using System.Globalization;
using System.Web;
using System.Web.UI;
using System.ComponentModel;

// CS0436: Core.Configuration compiles Mono's Consts too, and exposes it to this assembly as a friend.
// Both copies come from the same Consts.cs.in, so which one binds makes no difference.
#pragma warning disable 0436

namespace System.Web.UI.WebControls
{
	[ControlBuilder (typeof(MultiViewControlBuilder))]
	[Designer ("System.Web.UI.Design.WebControls.MultiViewDesigner, " + Consts.AssemblySystem_Design, "System.ComponentModel.Design.IDesigner")]
	[ToolboxData ("<{0}:MultiView runat=\"server\"></{0}:MultiView>")]
	[ParseChildren (typeof(View))]
	[DefaultEvent ("ActiveViewChanged")]
	public class MultiView: Control
	{
		public static readonly string NextViewCommandName = "NextView";
		public static readonly string PreviousViewCommandName = "PrevView";
		public static readonly string SwitchViewByIDCommandName = "SwitchViewByID";
		public static readonly string SwitchViewByIndexCommandName = "SwitchViewByIndex";

		static readonly object ActiveViewChangedEvent = new object();

		int activeViewIndex = -1;
		int cachedActiveViewIndex = -1;
		bool controlStateApplied;
		bool ignoreBubbleEvents;

		public event EventHandler ActiveViewChanged {
			add { Events.AddHandler (ActiveViewChangedEvent, value); }
			remove { Events.RemoveHandler (ActiveViewChangedEvent, value); }
		}

		protected override void AddParsedSubObject (object obj)
		{
			if (obj is View)
				Controls.Add ((Control) obj);
			else if (!(obj is LiteralControl))
				throw new HttpException ("MultiView cannot have children of type '" + obj.GetType ().Name + "'.  It can only have children of type View.");
		}

		protected override ControlCollection CreateControlCollection ()
		{
			return new ViewCollection (this);
		}

		public View GetActiveView ()
		{
			int index = ActiveViewIndex;
			if (index >= Views.Count)
				throw new Exception ("ActiveViewIndex is being set to '" + index + "'.  It must be smaller than the current number of View controls '" + Views.Count + "'. For dynamically added views, make sure they are added before or in Page_PreInit event.");
			if (index < 0)
				return null;

			View view = Views [index];
			if (!view.VisibleInternal)
				UpdateActiveView (index);
			return view;
		}

		public void SetActiveView (View view)
		{
			int index = Views.IndexOf (view);
			if (index < 0)
				throw new HttpException ("View '" + (view == null ? "null" : view.ID) + "' could not be found in MultiView '" + ID + "'.");

			ActiveViewIndex = index;
		}

		[DefaultValue (-1)]
		public virtual int ActiveViewIndex {
			get {
				if (cachedActiveViewIndex > -1)
					return cachedActiveViewIndex;
				return activeViewIndex;
			}
			set {
				if (value < -1)
					throw new ArgumentOutOfRangeException ("value", "ActiveViewIndex is being set to '" + value + "'.  It must be greater than or equal to -1.");

				// Assigned before the views exist - declaratively, or in code on a MultiView not yet in
				// the tree. Applied in OnInit.
				if (Views.Count == 0 && !IsInited) {
					cachedActiveViewIndex = value;
					return;
				}

				if (value >= Views.Count)
					throw new ArgumentOutOfRangeException ("value", "ActiveViewIndex is being set to '" + value + "'.  It must be smaller than the current number of View controls '" + Views.Count + "'.");

				// A cached index means no view was ever activated, so there is nothing to deactivate.
				int originalIndex = cachedActiveViewIndex != -1 ? -1 : activeViewIndex;
				activeViewIndex = value;
				cachedActiveViewIndex = -1;

				if (originalIndex != value && originalIndex != -1 && originalIndex < Views.Count) {
					Views [originalIndex].VisibleInternal = false;
					if (ShouldTriggerViewEvent)
						Views [originalIndex].NotifyActivation (false);
				}

				if (originalIndex != value && Views.Count != 0 && value != -1) {
					Views [value].VisibleInternal = true;
					if (ShouldTriggerViewEvent) {
						Views [value].NotifyActivation (true);
						OnActiveViewChanged (EventArgs.Empty);
					}
				}
			}
		}

		// Fire the views' events on the first request, and on a postback only once control state holds
		// the real index - never for the declarative index OnInit re-applies before that.
		bool ShouldTriggerViewEvent {
			get { return controlStateApplied || (Page != null && !Page.IsPostBack); }
		}

		[Browsable (true)]
		public virtual new bool EnableTheming
		{
			get { return base.EnableTheming; }
			set { base.EnableTheming = value; }
		}

		[PersistenceMode (PersistenceMode.InnerDefaultProperty)]
		[Browsable (false)]
		public virtual ViewCollection Views {
			get { return Controls as ViewCollection; }
		}

		// Wizard hosts its steps in a MultiView and does its own navigation.
		internal void IgnoreBubbleEvents ()
		{
			ignoreBubbleEvents = true;
		}

		protected override bool OnBubbleEvent (object source, EventArgs e)
		{
			if (ignoreBubbleEvents)
				return false;

			CommandEventArgs ca = e as CommandEventArgs;
			if (ca == null)
				return false;

			string name = ca.CommandName;

			if (name == NextViewCommandName) {
				if (ActiveViewIndex < Views.Count - 1)
					ActiveViewIndex = ActiveViewIndex + 1;
				else
					ActiveViewIndex = -1;
				return true;
			}

			if (name == PreviousViewCommandName) {
				if (ActiveViewIndex > -1)
					ActiveViewIndex = ActiveViewIndex - 1;
				return true;
			}

			if (name == SwitchViewByIDCommandName) {
				View view = FindControl ((string) ca.CommandArgument) as View;
				if (view == null || view.Parent != this)
					throw new HttpException ("MultiView '" + ID + "' does not contain a View control with ID '" + ca.CommandArgument + "' for command '" + SwitchViewByIDCommandName + "'.");
				SetActiveView (view);
				return true;
			}

			if (name == SwitchViewByIndexCommandName) {
				int index;
				try {
					index = Int32.Parse ((string) ca.CommandArgument, CultureInfo.InvariantCulture);
				} catch (FormatException) {
					throw new FormatException ("CommandArgument '" + ca.CommandArgument + "' is not a valid integer for command '" + SwitchViewByIndexCommandName + "'.");
				}
				ActiveViewIndex = index;
				return true;
			}

			return false;
		}

		protected internal override void OnInit (EventArgs e)
		{
			base.OnInit (e);

			Page.RegisterRequiresControlState (this);

			if (cachedActiveViewIndex > -1) {
				ActiveViewIndex = cachedActiveViewIndex;
				cachedActiveViewIndex = -1;
				GetActiveView ();
			}
		}

		void UpdateActiveView (int index)
		{
			for (int n = 0; n < Views.Count; n++) {
				View view = Views [n];
				if (n == index) {
					view.VisibleInternal = true;
					if (ShouldTriggerViewEvent)
						view.NotifyActivation (true);
				} else if (view.VisibleInternal) {
					view.VisibleInternal = false;
					if (ShouldTriggerViewEvent)
						view.NotifyActivation (false);
				}
			}
		}

		protected internal override void RemovedControl (Control ctl)
		{
			if (((View) ctl).VisibleInternal && ActiveViewIndex < Views.Count)
				GetActiveView ();

			base.RemovedControl (ctl);
		}

		protected internal override void LoadControlState (object state)
		{
			Pair pair = state as Pair;
			if (pair != null) {
				base.LoadControlState (pair.First);
				ActiveViewIndex = (int) pair.Second;
			}

			controlStateApplied = true;
		}

		protected internal override object SaveControlState ()
		{
			int index = ActiveViewIndex;
			object baseState = base.SaveControlState ();
			if (baseState != null || index != -1)
				return new Pair (baseState, index);
			return null;
		}

		protected virtual void OnActiveViewChanged (EventArgs e)
		{
			EventHandler eh = (EventHandler) Events [ActiveViewChangedEvent];
			if (eh != null)
				eh (this, e);
		}

		protected internal override void Render (HtmlTextWriter writer)
		{
			View view = GetActiveView ();
			if (view != null)
				view.RenderControl (writer);
		}
	}
}
