using System;
using System.Web.UI;

namespace WebFormsSample
{
	// A user-control code-behind + designer pair, so the nesting applied by the package's build/
	// props and targets is exercised for .ascx as well as .aspx. WidgetBox.ascx declares its own
	// members inline and does not use Inherits=, so this class is not wired to it - it exists to
	// exercise the project tree, not the runtime.
	public partial class WidgetBoxControl : UserControl
	{
	}
}
