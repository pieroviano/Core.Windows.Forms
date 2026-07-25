using System;
using System.Web.UI;

namespace WebFormsSample
{
	// A master-page code-behind + designer pair, so the nesting applied by the package's build/ props
	// and targets is exercised for .master as well as .aspx and .ascx. Site.master declares its
	// placeholders inline and does not use Inherits=, so this class is not wired to it - it exists to
	// exercise the project tree, not the runtime.
	public partial class SiteMaster : MasterPage
	{
	}
}
