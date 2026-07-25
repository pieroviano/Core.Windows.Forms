using System;
using System.Configuration;
using System.Text;

namespace ThirdPartyProbe
{
	public static class Probe
	{
		// Everything here binds to System.Configuration.ConfigurationManager at compile time.
		public static string Describe ()
		{
			var sb = new StringBuilder ();
			sb.Append ("resolvedFrom=")
			  .Append (typeof (ConfigurationManager).Assembly.GetName ().Name);

			try {
				var app = ConfigurationManager.AppSettings;
				sb.Append (" appSettings=").Append (app == null ? "<null>" : app.Count.ToString ());
				sb.Append (" probeSetting=").Append (app == null ? "<null>" : (app ["probe.setting"] ?? "<missing>"));
			} catch (Exception e) {
				sb.Append (" appSettings=EX:").Append (e.GetType ().Name);
			}

			try {
				var cs = ConfigurationManager.ConnectionStrings ["ProbeDb"];
				sb.Append (" connectionString=").Append (cs == null ? "<missing>" : cs.ConnectionString);
			} catch (Exception e) {
				sb.Append (" connectionString=EX:").Append (e.GetType ().Name);
			}

			return sb.ToString ();
		}
	}
}
