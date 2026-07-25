//
// One page driving Session through query-string operations, so an HTTP test can exercise the store
// without a browser.
//
// Everything here is ordinary System.Web code that predates the port by twenty years. That is the
// point: nothing on this page knows whether the session lives in the process, in Redis or in SQL
// Server, and it must not have to.
//

using System;
using System.Text;
using System.Web;
using System.Web.UI;

namespace SessionStateSample
{
	public partial class SessionPage : Page
	{
		protected void Page_Load (object sender, EventArgs e)
		{
			// Touched on every request so the session exists even for operations that only read -
			// otherwise a session id is handed out and immediately forgotten, and the test would see a
			// new one on every request.
			int hits = Session ["hits"] is int ? (int) Session ["hits"] : 0;
			Session ["hits"] = hits + 1;

			Mode.Text = Session.Mode.ToString ();
			Id.Text = Session.SessionID;
			Hits.Text = Session ["hits"].ToString ();
			Result.Text = Execute ();
		}

		string Execute ()
		{
			string operation = Request.QueryString ["op"] ?? "none";
			string key = Request.QueryString ["key"] ?? "value";

			switch (operation) {
			case "set":
				Session [key] = Request.QueryString ["value"];
				return "set " + key;

			case "get":
				object stored = Session [key];
				return stored == null ? "(null)" : stored.ToString ();

			case "setint":
				Session [key] = Int32.Parse (Request.QueryString ["value"]);
				return "set " + key;

			case "setdate":
				Session [key] = DateTime.Parse (Request.QueryString ["value"],
								System.Globalization.CultureInfo.InvariantCulture);
				return "set " + key;

			case "type":
				object typed = Session [key];
				return typed == null ? "(null)" : typed.GetType ().FullName;

			case "remove":
				Session.Remove (key);
				return "removed " + key;

			case "abandon":
				Session.Abandon ();
				return "abandoned";

			case "big":
				// Larger than ASPState's 7000-byte SessionItemShort column, which is what forces the
				// SessionItemLong path in SQL Server mode. Deterministic so the reader can verify it
				// came back byte for byte rather than merely came back.
				int size = Int32.Parse (Request.QueryString ["size"] ?? "20000");
				var builder = new StringBuilder (size);
				for (int i = 0; i < size; i++)
					builder.Append ((char) ('a' + (i % 26)));

				Session [key] = builder.ToString ();
				return "wrote " + size;

			case "verifybig":
				var expected = Request.QueryString ["value"];
				var actual = Session [key] as string;
				if (actual == null)
					return "(null)";

				int expectedSize = Int32.Parse (Request.QueryString ["size"] ?? "20000");
				if (actual.Length != expectedSize)
					return "length " + actual.Length + " expected " + expectedSize;

				for (int i = 0; i < actual.Length; i++) {
					if (actual [i] != (char) ('a' + (i % 26)))
						return "mismatch at " + i;
				}

				return "intact " + actual.Length;

			case "unserializable":
				// A type the state serializer has no faithful encoding for. InProc never noticed;
				// out-of-process has to write it down, so this is where a migration finds out.
				Session [key] = new NotSerializable ();
				return "stored";

			case "count":
				return Session.Count.ToString ();

			default:
				return "none";
			}
		}

		/// <summary>Deliberately has no serializable form - see the "unserializable" operation.</summary>
		public class NotSerializable
		{
			public Action Callback = () => { };
		}
	}
}
