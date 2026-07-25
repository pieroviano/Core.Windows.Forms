<%@ WebHandler Language="C#" Class="DownloadHandler" %>

using System;
using System.Globalization;
using System.IO;
using System.Web;

// Serves a generated file through TransmitFile - the path that must stream to the client rather than
// accumulate the response in memory. The file is generated on first use and cached by size, so the
// sample carries no large binary and a test can ask for one big enough to make buffering obvious.
public class DownloadHandler : IHttpHandler
{
	public const int DefaultSize = 3 * 1024 * 1024;

	// Bounded, because this is reachable by anyone who can reach the sample: a size taken from the
	// query string with no ceiling is an invitation to write an arbitrarily large file to disk.
	const int MaximumSize = 64 * 1024 * 1024;

	public bool IsReusable { get { return true; } }

	public void ProcessRequest (HttpContext context)
	{
		int size = DefaultSize;
		string requested = context.Request.QueryString ["size"];
		int parsed;

		if (!String.IsNullOrEmpty (requested) &&
		    Int32.TryParse (requested, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
			size = Math.Max (1, Math.Min (parsed, MaximumSize));

		string path = Path.Combine (
			Path.GetTempPath (),
			"webforms-sample-download-" + size.ToString (CultureInfo.InvariantCulture) + ".bin");

		if (!File.Exists (path) || new FileInfo (path).Length != size) {
			var bytes = new byte [size];
			for (int i = 0; i < bytes.Length; i++)
				bytes [i] = (byte) (i % 251);     // 251 is prime, so the pattern never aligns with
			File.WriteAllBytes (path, bytes);         // a power-of-two chunk boundary
		}

		context.Response.ContentType = "application/octet-stream";
		context.Response.TransmitFile (path);
	}
}
