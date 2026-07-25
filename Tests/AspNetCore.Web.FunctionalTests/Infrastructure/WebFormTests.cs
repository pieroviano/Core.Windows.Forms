//
// Unit tests for the test helpers themselves. No server, no fixture.
//
// These earn their place because both helpers are parsers with a failure mode that would otherwise be
// silent: a regex that quietly matches nothing produces an empty postback, and the functional test
// then fails somewhere else entirely, blaming the port for a bug in the harness.
//

using System.Collections.Generic;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	public class WebFormTests
	{
		[Fact]
		public void HiddenFields_reads_name_and_value ()
		{
			const string html = @"<div class=""aspNetHidden"">
<input type=""hidden"" name=""__VIEWSTATE"" id=""__VIEWSTATE"" value=""/wEMDAwQAg=="" />
</div>";

			Dictionary<string, string> fields = WebForm.HiddenFields (html);

			Assert.Equal ("/wEMDAwQAg==", Assert.Contains ("__VIEWSTATE", fields));
		}

		[Fact]
		public void HiddenFields_ignores_non_hidden_inputs ()
		{
			const string html = @"<input type=""text"" name=""who"" value=""piero"" />
<input type=""hidden"" name=""__EVENTTARGET"" id=""__EVENTTARGET"" value="""" />
<input type=""submit"" name=""greet"" value=""Greet"" id=""greet"" />";

			Dictionary<string, string> fields = WebForm.HiddenFields (html);

			Assert.True (fields.ContainsKey ("__EVENTTARGET"));
			Assert.False (fields.ContainsKey ("who"));
			Assert.False (fields.ContainsKey ("greet"));
		}

		[Fact]
		public void HiddenFields_decodes_html_entities_in_values ()
		{
			const string html = @"<input type=""hidden"" name=""x"" value=""a &amp; b"" />";

			Assert.Equal ("a & b", WebForm.HiddenFields (html) ["x"]);
		}

		[Fact]
		public void HiddenFields_returns_empty_when_there_is_no_form ()
		{
			// A page with no <form runat="server"> renders no hidden fields at all. Empty, not a throw.
			Assert.Empty (WebForm.HiddenFields ("<html><body><h1>Simple page</h1></body></html>"));
		}

		[Fact]
		public void ParseDelta_reads_type_id_and_content ()
		{
			const string body = "5|updatePanel|up|hello|0|hiddenField|__EVENTTARGET||";

			List<WebForm.DeltaSegment> segments = WebForm.ParseDelta (body);

			Assert.Equal (2, segments.Count);
			Assert.Equal ("updatePanel", segments [0].Type);
			Assert.Equal ("up", segments [0].Id);
			Assert.Equal ("hello", segments [0].Content);
			Assert.Equal ("hiddenField", segments [1].Type);
			Assert.Equal ("__EVENTTARGET", segments [1].Id);
			Assert.Equal ("", segments [1].Content);
		}

		[Fact]
		public void ParseDelta_handles_content_containing_the_separator ()
		{
			// The reason the format is length-prefixed rather than delimited. Splitting on '|' would
			// mangle this, and rendered markup really does contain '|'.
			const string content = "a|b|c";
			string body = content.Length + "|updatePanel|up|" + content + "|";

			WebForm.DeltaSegment segment = Assert.Single (WebForm.ParseDelta (body));

			Assert.Equal (content, segment.Content);
		}

		[Fact]
		public void ParseDelta_rejects_a_full_html_page ()
		{
			// The likeliest real failure: the request was not treated as a partial postback and the
			// server rendered a document. Say so, rather than returning nonsense segments.
			System.FormatException error = Assert.Throws<System.FormatException> (
				() => WebForm.ParseDelta ("<!DOCTYPE html>\n<html><body>oops</body></html>"));

			Assert.Contains ("partial postback", error.Message);
		}

		[Fact]
		public void DeltaHiddenFields_selects_only_hidden_field_records ()
		{
			const string body = "5|updatePanel|up|hello|4|hiddenField|__VIEWSTATE|abcd|0|pageTitle||";

			Dictionary<string, string> fields = WebForm.DeltaHiddenFields (body);

			Assert.Equal ("abcd", Assert.Contains ("__VIEWSTATE", fields));
			Assert.Single (fields);
		}
	}
}
