using System;

namespace WebFormsSample.AppCode
{
	// Files under App_Code are compiled by BuildManager into their own assembly at runtime (not by
	// the SDK), and pages can use them without any reference. That makes this a test of
	// AppCodeCompiler going through the Roslyn backend.
	public static class Helper
	{
		public static string Shout (string s)
		{
			return (s ?? String.Empty).ToUpperInvariant () + "!";
		}
	}
}
