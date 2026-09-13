<%@ Page Language="C#" Inherits="WebFormsSample.EventLogPage" AutoEventWireup="true" %>
<%--
  Validation, client and server.

  With script enabled the validators run in the browser (WebUIValidation.js) and an invalid form is
  never submitted. With script disabled the form IS submitted, and Button.RaisePostBackEvent runs
  Page.Validate (ValidationGroup) before raising Click - so the handler runs either way, and has to
  consult Page.IsValid itself. Rules exercised:

    - only RequiredFieldValidator fails an empty field; every other validator treats empty as valid,
      and CustomValidator does not even raise ServerValidate for it (ValidateEmptyText="false");
    - a ValidationGroup validates only the validators in that group;
    - CausesValidation="false" skips validation entirely.
--%>
<script runat="server">
    protected void CodeServerValidate (object source, ServerValidateEventArgs args)
    {
        Log ("code.ServerValidate(" + args.Value + ")");
        args.IsValid = args.Value.Length % 2 == 0;
    }

    protected void SubmitClick (object sender, EventArgs e) { Log ("submit.Click(IsValid=" + IsValid + ")"); }
    protected void SkipClick (object sender, EventArgs e)   { Log ("skip.Click"); }
    protected void FindClick (object sender, EventArgs e)   { Log ("find.Click(IsValid=" + IsValid + ")"); }
</script>
<!DOCTYPE html>
<html>
<head runat="server">
  <title>Validation</title>
  <script type="text/javascript">
    function evenLength(source, args) { args.IsValid = args.Value.length % 2 === 0; }
  </script>
</head>
<body>
  <h1>Validation</h1>
  <form id="form1" runat="server">
    <asp:ValidationSummary ID="summary" runat="server" HeaderText="Please fix:" />

    <p><label for="name">Name</label> <asp:TextBox ID="name" runat="server" />
      <asp:RequiredFieldValidator ID="nameRequired" runat="server" ControlToValidate="name"
          ErrorMessage="Name is required" Text="*" Display="Dynamic" /></p>

    <p><label for="age">Age</label> <asp:TextBox ID="age" runat="server" />
      <asp:RangeValidator ID="ageRange" runat="server" ControlToValidate="age" Type="Integer"
          MinimumValue="18" MaximumValue="99" ErrorMessage="Age must be 18-99" Text="*" Display="Dynamic" /></p>

    <p><label for="email">Email</label> <asp:TextBox ID="email" runat="server" />
      <asp:RegularExpressionValidator ID="emailFormat" runat="server" ControlToValidate="email"
          ValidationExpression="[^@\s]+@[^@\s]+\.[a-z]+" ErrorMessage="Email is malformed" Text="*" Display="Dynamic" /></p>

    <p><label for="pwd">Password</label> <asp:TextBox ID="pwd" runat="server" />
      <label for="confirm">Confirm</label> <asp:TextBox ID="confirm" runat="server" />
      <asp:CompareValidator ID="confirmMatches" runat="server" ControlToValidate="confirm" ControlToCompare="pwd"
          ErrorMessage="Passwords differ" Text="*" Display="Dynamic" /></p>

    <p><label for="code">Code (even length)</label> <asp:TextBox ID="code" runat="server" />
      <asp:CustomValidator ID="codeEven" runat="server" ControlToValidate="code"
          ClientValidationFunction="evenLength" OnServerValidate="CodeServerValidate"
          ErrorMessage="Code must have an even length" Text="*" Display="Dynamic" /></p>

    <p>
      <asp:Button ID="submit" runat="server" Text="Submit" OnClick="SubmitClick" />
      <asp:Button ID="skip" runat="server" Text="Skip validation" CausesValidation="false" OnClick="SkipClick" />
    </p>

    <fieldset>
      <legend>Search (its own validation group)</legend>
      <asp:TextBox ID="search" runat="server" ValidationGroup="search" />
      <asp:RequiredFieldValidator ID="searchRequired" runat="server" ControlToValidate="search"
          ValidationGroup="search" ErrorMessage="Search term is required" Display="Dynamic" />
      <asp:Button ID="find" runat="server" Text="Find" ValidationGroup="search" OnClick="FindClick" />
    </fieldset>

    <h2>Events</h2>
    <p>Postbacks: <asp:Label ID="postbacks" runat="server" /></p>
    <p><asp:Label ID="log" runat="server" /></p>
  </form>
</body>
</html>
