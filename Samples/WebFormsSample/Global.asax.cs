using System;
using System.Web;

namespace WebFormsSample
{
	// The classic Global.asax code-behind shape. Global.asax here keeps its handlers inline in a
	// <script runat="server"> block and declares no Inherits=, so the runtime compiles and uses that
	// one; this class exists to exercise the .asax nesting in the project tree.
	public class GlobalApplication : HttpApplication
	{
	}
}
