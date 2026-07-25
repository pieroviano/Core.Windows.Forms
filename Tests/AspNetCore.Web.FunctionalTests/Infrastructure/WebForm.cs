//
// Minimal helpers for driving a WebForms postback over HTTP.
//
// A postback is not just "POST the page": ASP.NET requires the hidden fields it rendered to come
// back verbatim. __VIEWSTATE carries the control tree's persisted state (and is MAC-signed, so it
// cannot be synthesised), and __EVENTVALIDATION is the registered-event whitelist - post without it
// and the request is rejected before the page's own code runs. These helpers scrape the rendered
// form and hand back a body that keeps them.
//

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace WebFormsPort
{
	static class WebForm
	{
		static readonly Regex HiddenInput = new Regex (
			@"<input\b[^>]*type=""hidden""[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		static readonly Regex Attribute = new Regex (
			@"\b(?<name>name|value)=""(?<value>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		/// <summary>
		/// Every hidden input in the rendered page, by name. That is __VIEWSTATE and
		/// __EVENTVALIDATION, plus __VIEWSTATEGENERATOR and the __EVENTTARGET/__EVENTARGUMENT pair
		/// when the page emits them.
		/// </summary>
		public static Dictionary<string, string> HiddenFields (string html)
		{
			var fields = new Dictionary<string, string> (StringComparer.Ordinal);

			foreach (Match input in HiddenInput.Matches (html)) {
				string name = null, value = String.Empty;

				foreach (Match attribute in Attribute.Matches (input.Value)) {
					if (String.Equals (attribute.Groups ["name"].Value, "name", StringComparison.OrdinalIgnoreCase))
						name = attribute.Groups ["value"].Value;
					else
						value = attribute.Groups ["value"].Value;
				}

				// Values are HTML-encoded in the markup. Base64 view state never contains a
				// character that encodes, but ordinary hidden fields can.
				if (!String.IsNullOrEmpty (name))
					fields [name] = System.Net.WebUtility.HtmlDecode (value);
			}

			return fields;
		}

		/// <summary>
		/// Builds a postback body: the page's hidden fields, plus the supplied form values. Passing
		/// the submit button's name and value is what makes the server raise its Click event.
		/// </summary>
		public static FormUrlEncodedContent Postback (string html, params (string Name, string Value) [] values)
		{
			Dictionary<string, string> fields = HiddenFields (html);

			foreach ((string name, string value) in values)
				fields [name] = value;

			return new FormUrlEncodedContent (fields);
		}

		/// <summary>
		/// Builds an ASP.NET AJAX partial ("async") postback body.
		/// </summary>
		/// <param name="html">the rendered page, for its hidden fields</param>
		/// <param name="scriptManagerId">the ScriptManager's UniqueID; its value selects the panel</param>
		/// <param name="updatePanelId">UpdatePanel to refresh</param>
		/// <param name="targetId">control raising the postback</param>
		/// <remarks>
		/// The two fields that make this a partial postback rather than a normal one are
		/// __ASYNCPOST=true and "&lt;scriptManagerId&gt;=&lt;panel&gt;|&lt;target&gt;". Without them the
		/// server renders the whole page and the response is ordinary HTML.
		/// </remarks>
		public static FormUrlEncodedContent AsyncPostback (string html, string scriptManagerId,
								   string updatePanelId, string targetId,
								   params (string Name, string Value) [] values)
		{
			Dictionary<string, string> fields = HiddenFields (html);

			fields [scriptManagerId] = updatePanelId + "|" + targetId;
			fields ["__EVENTTARGET"] = targetId;
			fields ["__EVENTARGUMENT"] = String.Empty;
			fields ["__ASYNCPOST"] = "true";

			foreach ((string name, string value) in values)
				fields [name] = value;

			return new FormUrlEncodedContent (fields);
		}

		/// <summary>One record in a partial-rendering response.</summary>
		public readonly struct DeltaSegment
		{
			public DeltaSegment (string type, string id, string content)
			{
				Type = type;
				Id = id;
				Content = content;
			}

			/// <summary>"updatePanel", "hiddenField", "pageTitle", ...</summary>
			public string Type { get; }
			public string Id { get; }
			public string Content { get; }
		}

		/// <summary>
		/// Parses a partial-rendering response into its segments.
		/// </summary>
		/// <remarks>
		/// The wire format is a repeated "length|type|id|content|", where length counts the CHARACTERS
		/// of content. Splitting on '|' does not work and is the obvious wrong way to read this - the
		/// length prefix exists precisely because rendered markup contains '|'. Read the length, then
		/// take exactly that many characters.
		/// </remarks>
		public static List<DeltaSegment> ParseDelta (string body)
		{
			var segments = new List<DeltaSegment> ();
			int i = 0;

			while (i < body.Length) {
				string length = ReadField (body, ref i);
				string type = ReadField (body, ref i);
				string id = ReadField (body, ref i);

				if (!int.TryParse (length, out int count) || i + count > body.Length)
					throw new FormatException (
						$"malformed partial-rendering response at offset {i}: length '{length}'. " +
						"A full HTML page here means the request was not treated as a partial postback.");

				segments.Add (new DeltaSegment (type, id, body.Substring (i, count)));
				i += count + 1;   // + the trailing '|'
			}

			return segments;
		}

		/// <summary>
		/// The hidden fields carried in a partial-rendering response, so a second partial postback can
		/// be built from the first one's reply.
		/// </summary>
		public static Dictionary<string, string> DeltaHiddenFields (string body)
		{
			var fields = new Dictionary<string, string> (StringComparer.Ordinal);

			foreach (DeltaSegment segment in ParseDelta (body)) {
				if (segment.Type == "hiddenField")
					fields [segment.Id] = segment.Content;
			}

			return fields;
		}

		/// <summary>
		/// Builds the next partial postback from the previous partial response, which carries updated
		/// hidden fields in its own format rather than as markup.
		/// </summary>
		public static FormUrlEncodedContent AsyncPostbackFromDelta (string body, string scriptManagerId,
									    string updatePanelId, string targetId,
									    params (string Name, string Value) [] values)
		{
			Dictionary<string, string> fields = DeltaHiddenFields (body);

			fields [scriptManagerId] = updatePanelId + "|" + targetId;
			fields ["__EVENTTARGET"] = targetId;
			fields ["__EVENTARGUMENT"] = String.Empty;
			fields ["__ASYNCPOST"] = "true";

			foreach ((string name, string value) in values)
				fields [name] = value;

			return new FormUrlEncodedContent (fields);
		}

		static string ReadField (string body, ref int i)
		{
			int end = body.IndexOf ('|', i);
			if (end < 0)
				throw new FormatException (
					$"malformed partial-rendering response: no '|' after offset {i}. " +
					"A full HTML page here means the request was not treated as a partial postback " +
					"- check that __ASYNCPOST and the ScriptManager field were sent.");

			string value = body.Substring (i, end - i);
			i = end + 1;
			return value;
		}
	}
}
