<%@ Page Language="C#" Inherits="WebFormsSample.EventLogPage" AutoEventWireup="true" %>
<%--
  Controls that navigate by handling bubbled commands themselves.

  MultiView (MultiView.OnBubbleEvent) understands NextView, PrevView, SwitchViewByID and
  SwitchViewByIndex from any button inside it. Changing the active view raises Deactivate on the old
  view, Activate on the new one, then ActiveViewChanged. On the FIRST request those events are raised
  too, from MultiView.OnInit, because there was no previous view (ShouldTriggerViewEvent); on a postback
  the declarative index is re-applied silently before control state restores the real one.

  Wizard raises NextButtonClick / PreviousButtonClick / FinishButtonClick, and ActiveStepChanged when
  the step actually moves. Calendar posts back through __doPostBack with the day, or with "V" plus the
  month for navigation.
--%>
<script runat="server">
    void Page_Load (object sender, EventArgs e)
    {
        if (!IsPostBack)
            cal.VisibleDate = new DateTime (2026, 3, 1);
    }

    protected void MvChanged (object sender, EventArgs e) { Log ("mv.ActiveViewChanged(" + mv.ActiveViewIndex + ")"); }
    protected void ViewActivate (object sender, EventArgs e) { Log (((View) sender).ID + ".Activate"); }
    protected void ViewDeactivate (object sender, EventArgs e) { Log (((View) sender).ID + ".Deactivate"); }

    protected void WizNext (object sender, WizardNavigationEventArgs e) { Log ("wiz.NextButtonClick(" + e.CurrentStepIndex + "->" + e.NextStepIndex + ")"); }
    protected void WizPrevious (object sender, WizardNavigationEventArgs e) { Log ("wiz.PreviousButtonClick(" + e.CurrentStepIndex + "->" + e.NextStepIndex + ")"); }
    protected void WizFinish (object sender, WizardNavigationEventArgs e) { Log ("wiz.FinishButtonClick(" + e.CurrentStepIndex + "->" + e.NextStepIndex + ")"); }
    protected void WizStepChanged (object sender, EventArgs e) { Log ("wiz.ActiveStepChanged(" + wiz.ActiveStepIndex + ")"); }

    protected void CalSelectionChanged (object sender, EventArgs e) { Log ("cal.SelectionChanged(" + cal.SelectedDate.ToString ("yyyy-MM-dd") + ")"); }
    protected void CalMonthChanged (object sender, MonthChangedEventArgs e)
    {
        Log ("cal.VisibleMonthChanged(" + e.PreviousDate.ToString ("yyyy-MM") + "->" + e.NewDate.ToString ("yyyy-MM") + ")");
    }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>Views, wizard and calendar</title></head>
<body>
  <h1>Views, wizard and calendar</h1>
  <form id="form1" runat="server">
    <h2>MultiView</h2>
    <asp:MultiView ID="mv" runat="server" ActiveViewIndex="0" OnActiveViewChanged="MvChanged">
      <asp:View ID="v1" runat="server" OnActivate="ViewActivate" OnDeactivate="ViewDeactivate">
        <p class="view">View one</p>
        <asp:Button ID="toTwo" runat="server" Text="Next view" CommandName="NextView" />
      </asp:View>
      <asp:View ID="v2" runat="server" OnActivate="ViewActivate" OnDeactivate="ViewDeactivate">
        <p class="view">View two</p>
        <asp:Button ID="backToOne" runat="server" Text="Previous view" CommandName="PrevView" />
        <asp:Button ID="toThree" runat="server" Text="Jump to three" CommandName="SwitchViewByID" CommandArgument="v3" />
      </asp:View>
      <asp:View ID="v3" runat="server" OnActivate="ViewActivate" OnDeactivate="ViewDeactivate">
        <p class="view">View three</p>
        <asp:Button ID="toFirst" runat="server" Text="Back to first" CommandName="SwitchViewByIndex" CommandArgument="0" />
      </asp:View>
    </asp:MultiView>

    <h2>Wizard</h2>
    <asp:Wizard ID="wiz" runat="server" DisplaySideBar="false"
                OnNextButtonClick="WizNext" OnPreviousButtonClick="WizPrevious"
                OnFinishButtonClick="WizFinish" OnActiveStepChanged="WizStepChanged">
      <WizardSteps>
        <asp:WizardStep ID="s1" runat="server" Title="One"><p class="step">Step one</p></asp:WizardStep>
        <asp:WizardStep ID="s2" runat="server" Title="Two"><p class="step">Step two</p></asp:WizardStep>
        <asp:WizardStep ID="s3" runat="server" Title="Three"><p class="step">Step three</p></asp:WizardStep>
      </WizardSteps>
    </asp:Wizard>

    <h2>Calendar</h2>
    <asp:Calendar ID="cal" runat="server" OnSelectionChanged="CalSelectionChanged"
                  OnVisibleMonthChanged="CalMonthChanged" />

    <h2>Events</h2>
    <p>Postbacks: <asp:Label ID="postbacks" runat="server" /></p>
    <p><asp:Label ID="log" runat="server" /></p>
  </form>
</body>
</html>
