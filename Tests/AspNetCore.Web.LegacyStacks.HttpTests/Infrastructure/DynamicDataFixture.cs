//
// Starts Samples/DynamicDataSample on a real Kestrel server, once for the assembly.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Web.Hosting.Kestrel;
using Xunit;

namespace WebFormsPort.LegacyStacksHttpTests
{
	public sealed class DynamicDataFixture : IAsyncLifetime
	{
		WebApplication app;

		public string BaseAddress { get; private set; }

		public async Task InitializeAsync ()
		{
			string appPath = RepoPaths.Sample ("DynamicDataSample");

			// Keyed on this assembly, not on the application path: the default is keyed on the path, and
			// a second suite hosting the same sample would corrupt its generated pages.
			string temp = Path.Combine (Path.GetTempPath (), "webforms-tests",
						    typeof (DynamicDataFixture).Assembly.GetName ().Name);
			if (Directory.Exists (temp))
				Directory.Delete (temp, recursive: true);

			var builder = WebApplication.CreateBuilder (new WebApplicationOptions {
				ContentRootPath = appPath,
				ApplicationName = typeof (DynamicDataFixture).Assembly.GetName ().Name,
			});

			builder.WebHost.UseUrls ("http://127.0.0.1:0");
			builder.Logging.ClearProviders ();

			app = builder.Build ();
			app.UseStaticFiles ();
			app.UseWebForms (options => {
				options.PhysicalPath = appPath;
				options.VirtualPath = "/";
				options.SiteName = "DynamicDataHttpTests";
				options.TemporaryFilesPath = temp;
				// ContextTypeName and RegisterContext resolve ShopContext by name through BuildManager,
				// which searches this list. The sample assembly is not the entry assembly here.
				options.ApplicationAssemblies = new [] {
					typeof (DynamicDataSample.Models.ShopContext).Assembly,
				};
			});

			await app.StartAsync ();
			BaseAddress = app.Urls.First ();
		}

		public async Task DisposeAsync ()
		{
			if (app == null)
				return;

			await app.StopAsync ();
			await app.DisposeAsync ();
		}

		public HttpClient CreateClient ()
		{
			return new HttpClient (new HttpClientHandler {
				UseCookies = true,
				CookieContainer = new CookieContainer (),
				AllowAutoRedirect = false,
			}) {
				BaseAddress = new Uri (BaseAddress),
			};
		}
	}

	//
	// Postbacks need the hidden fields back verbatim, and the port refuses a view state it did not
	// write, so they cannot be faked. Parsing the form out of the response is the only honest way to
	// drive an edit or a delete over HTTP.
	//
	static class Postback
	{
		static readonly Regex HiddenField = new Regex (
			"<input[^>]*type=\"hidden\"[^>]*>", RegexOptions.IgnoreCase);

		static readonly Regex Attribute = new Regex (
			"(?<name>name|value)=\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase);

		public static Dictionary<string, string> HiddenFields (string html)
		{
			var fields = new Dictionary<string, string> ();

			foreach (Match input in HiddenField.Matches (html)) {
				string name = null, value = "";

				foreach (Match attribute in Attribute.Matches (input.Value)) {
					if (attribute.Groups ["name"].Value.Equals ("name", StringComparison.OrdinalIgnoreCase))
						name = attribute.Groups ["value"].Value;
					else
						value = attribute.Groups ["value"].Value;
				}

				if (name != null)
					fields [name] = WebUtility.HtmlDecode (value);
			}

			return fields;
		}

		// __doPostBack (target, argument) is what every GridView command button calls. Reproducing it is
		// two more form fields on top of whatever was hidden on the page.
		public static FormUrlEncodedContent For (string html, string target, string argument = "",
							 params (string Name, string Value) [] extra)
		{
			Dictionary<string, string> fields = HiddenFields (html);
			fields ["__EVENTTARGET"] = target;
			fields ["__EVENTARGUMENT"] = argument;

			foreach ((string name, string value) in extra)
				fields [name] = value;

			return new FormUrlEncodedContent (fields);
		}

		// Posts back the command link LABELLED with the given text - "Edit", "Update", "Cancel".
		//
		// Which target and argument a GridView command uses is the grid's business, not the test's: Edit
		// renders as __doPostBack ("Grid", "Edit$0"), while Update renders as a LinkButton posting back
		// to its OWN unique id with an empty argument. Hard-coding either shape makes the test assert on
		// the framework's rendering strategy instead of on the behaviour, and fails event validation the
		// moment the strategy differs - which is the framework correctly refusing an event the page never
		// rendered. So the link is read out of the markup by its label.
		public static FormUrlEncodedContent Command (string html, string label,
							     params (string Name, string Value) [] extra)
		{
			Match link = Regex.Matches (html, "<a[^>]*__doPostBack\\(&#39;(?<target>[^&]*)&#39;,&#39;(?<arg>[^&]*)&#39;\\)[^>]*>(?<text>[^<]*)</a>")
					  .Cast<Match> ()
					  .FirstOrDefault (m => m.Groups ["text"].Value.Trim () == label);

			if (link == null)
				throw new InvalidOperationException (
					"No command link labelled '" + label + "' was rendered. Links present: " +
					String.Join (", ", Regex.Matches (html, "<a[^>]*__doPostBack[^>]*>(?<text>[^<]*)</a>")
								.Select (m => m.Groups ["text"].Value.Trim ())));

			return For (html, link.Groups ["target"].Value, link.Groups ["arg"].Value, extra);
		}

		// The text of every <td> in document order, tags stripped. Enough to assert on a grid's contents
		// without pulling in an HTML parser for four pages.
		public static string [] Cells (string html)
		{
			return Regex.Matches (html, "<td[^>]*>(?<text>.*?)</td>", RegexOptions.Singleline)
				    .Select (m => WebUtility.HtmlDecode (Regex.Replace (m.Groups ["text"].Value, "<[^>]+>", "")).Trim ())
				    .ToArray ();
		}
	}

	[CollectionDefinition (Name)]
	public sealed class DynamicDataCollection : ICollectionFixture<DynamicDataFixture>
	{
		public const string Name = "dynamicdata-sample";
	}
}
