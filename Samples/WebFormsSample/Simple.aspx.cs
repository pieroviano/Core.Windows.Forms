using System;
using System.Web.UI;

namespace WebFormsSample
{
	// Deliberately a code-behind + designer pair, so the nesting the package's build/ props and targets
	// apply has something to act on. Simple.aspx does not use Inherits=, so this class is not wired to
	// it - it exists to exercise the project-tree nesting, not the runtime.
	public partial class SimplePage : Page
	{
	}
}
