using System;
using System.Web.UI;

namespace LegacyWebForms
{
	public partial class DefaultPage : Page
	{
		protected void Page_Load (object sender, EventArgs e)
		{
			Message.Text = "hello from code-behind";
		}
	}
}
