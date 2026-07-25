//
// System.Web.UplevelHelper
//
// Upstream builds this file with mono/mcs/tools/culevel (CompileUplevel.cs, 955 lines) from
// mono/mcs/class/System.Web/UplevelHelperDefinitions.xml. The rule set is 12 entries long and the
// generator is not worth porting, so this is a hand translation of that XML. Keep the two in sync
// if UplevelHelperDefinitions.xml ever changes.
//
// Semantics taken from the generator, not guessed:
//   * positions="X-Y" match="abcd"  -> ua[X..Y] equals the match, ordinal (Y-X+1 == match.Length)
//   * positions="A-B,C" match="x,y" -> both position ranges must match their comma-separated part
//   * scanfrom="S" skip="K" match=".."
//         -> GenerateScanfromExpression (CompileUplevel.cs:707) emits a length guard of
//            S + K + match.Length + 1 and then scans from index 0, so this is an ordinal
//            IndexOf with a minimum-length precondition. S/K do not offset the search.
//   * <except> reverses its enclosing group's default and is evaluated before the subgroups.
//   * a group whose javascript attribute is absent defaults to false.
//
// HtmlForm.DetermineRenderUplevel and BaseValidator.DetermineRenderUplevel call this to decide
// whether to emit client-side validation script.
//

using System;

namespace System.Web
{
	sealed class UplevelHelper
	{
		UplevelHelper ()
		{
		}

		public static bool IsUplevel (string ua)
		{
			if (ua == null)
				return false;
			if (ua.Length == 0)
				return false;

			// <group positions="0-3" match="Mozi"> - Mozilla and compatibles, default false
			if (At (ua, 0, "Mozi")) {
				// <group positions="7-10" match="/4.0" javascript="true"> - MSIE, Netscape 4.0, Opera
				if (At (ua, 7, "/4.0")) {
					// <except positions="13-28" match="ActiveTouristBot"/>
					if (At (ua, 13, "ActiveTouristBot"))
						return false;
					return true;
				}

				// <group positions="7-10" match="/5.0" javascript="true"> - MSIE 10, MSIE 11
				if (At (ua, 7, "/5.0")) {
					// <except positions="13-28" match="ActiveTouristBot"/>
					if (At (ua, 13, "ActiveTouristBot"))
						return false;
					return true;
				}

				// <group scanfrom="12" skip="4" match=") Gecko/" javascript="true">
				if (Scan (ua, 25, ") Gecko/"))
					return true;

				// <group scanfrom="12" skip="4" match=") Opera" javascript="true">
				if (Scan (ua, 24, ") Opera"))
					return true;

				// <group positions="12-15" match="(Mac" javascript="true"> - Safari
				if (At (ua, 12, "(Mac"))
					return true;

				// <group scanfrom="12" skip="2" match="(KHTML" javascript="true"> - Safari
				if (Scan (ua, 21, "(KHTML"))
					return true;

				// <group positions="12-15" match="Gale" javascript="true"> - Galeon < 2.0
				if (At (ua, 12, "Gale"))
					return true;

				// <group positions="25-28" match="Konq" javascript="true"> - Konqueror
				if (At (ua, 25, "Konq"))
					return true;

				// <group positions="9-11,12" match="/4.,[" javascript="true"> - Netscape 4.x
				if (At (ua, 9, "/4.") && At (ua, 12, "["))
					return true;

				return false;
			}

			// <group positions="0-3" match="Konq" javascript="true"> - Konqueror
			if (At (ua, 0, "Konq"))
				return true;

			// <group positions="0-3" match="Oper" javascript="true"> - Opera
			if (At (ua, 0, "Oper"))
				return true;

			return false;
		}

		// ua[start .. start+match.Length-1] == match, ordinal
		static bool At (string ua, int start, string match)
		{
			int len = match.Length;
			if (ua.Length < start + len)
				return false;
			return String.CompareOrdinal (ua, start, match, 0, len) == 0;
		}

		static bool Scan (string ua, int minLength, string match)
		{
			if (ua.Length < minLength)
				return false;
			return ua.IndexOf (match, StringComparison.Ordinal) >= 0;
		}
	}
}
