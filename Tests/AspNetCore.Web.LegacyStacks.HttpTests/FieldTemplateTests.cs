//
// Dynamic Data field templates: the .ascx controls that decide how a column looks, and the
// DynamicField/DynamicControl plumbing that reaches them.
//

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.LegacyStacksHttpTests
{
	[Collection (DynamicDataCollection.Name)]
	public class FieldTemplateTests
	{
		readonly DynamicDataFixture fixture;

		public FieldTemplateTests (DynamicDataFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task A_DynamicField_renders_through_the_field_template ()
		{
			// The whole chain in one assertion: DynamicField -> DynamicControl -> FieldTemplateFactory
			// resolves Text.ascx by the column's type -> FieldTemplateUserControl.FieldValueString reads
			// the value off the row. Any link missing and the cell renders empty, with no error - which is
			// exactly how this failed before.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products/List.aspx");

			string [] cells = Postback.Cells (html);

			Assert.Contains ("Anvil", cells);
			Assert.Contains ("49.95", cells);
		}

		[Fact]
		public async Task The_template_is_chosen_by_the_column_type ()
		{
			// Boolean.ascx for the bool column, Text.ascx for everything else - by type fallback, not by
			// name. Product has one bool (Discontinued) and four other scaffolded columns, so a checkbox
			// per row and no checkbox anywhere else.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products/List.aspx");

			int checkboxes = System.Text.RegularExpressions.Regex.Matches (html, "type=\"checkbox\"").Count;
			Assert.Equal (4, checkboxes);

			// Categories has no bool at all, so the same page template renders none.
			string categories = await client.GetStringAsync ("/Categories/List.aspx");
			Assert.DoesNotContain ("type=\"checkbox\"", categories);
		}

		[Fact]
		public async Task Editing_a_row_switches_to_the_edit_template ()
		{
			// FieldTemplateFactory appends "_Edit" to the template name in edit mode, so the same column
			// renders through Text_Edit.ascx - a TextBox - instead of Text.ascx.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products/List.aspx");

			HttpResponseMessage response = await client.PostAsync (
				"/Products/List.aspx", Postback.Command (html, "Edit"));
			string edit = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);
			Assert.Contains ("type=\"text\"", edit);
			Assert.Contains ("value=\"Anvil\"", edit);
		}

		[Fact]
		public async Task A_read_only_column_stays_read_only_in_edit_mode ()
		{
			// PreprocessMode forces ReadOnly for a generated column. Product.Id is an int primary key, so
			// the provider marks it generated and MetaColumn.Scaffold hides it entirely - but the rule
			// that matters here is the one for a column that IS scaffolded and NOT editable. Category.Id
			// is [DatabaseGenerated(None)] and IS the primary key, so it is scaffolded and must not become
			// a text box someone can retype.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Categories/List.aspx");

			HttpResponseMessage response = await client.PostAsync (
				"/Categories/List.aspx", Postback.Command (html, "Edit"));
			string edit = await response.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);

			// Exactly one editable control in the row: Name. The key renders through the read-only
			// template, so counting is the assertion - "does not contain 1" would pass by accident on a
			// page full of ones.
			Assert.Contains ("value=\"Hardware\"", edit);
			Assert.Single (System.Text.RegularExpressions.Regex.Matches (edit, "type=\"text\""));
		}

		[Fact]
		public async Task An_edited_value_reaches_the_page_through_ExtractValuesFromCell ()
		{
			// The end of the chain, and the member that used to throw NotImplementedException on every
			// update: the page never touches a TextBox - it reads e.NewValues, which GridView fills by
			// asking each DynamicField for its cell's value, which asks the field template.
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products/List.aspx");

			HttpResponseMessage editing = await client.PostAsync (
				"/Products/List.aspx", Postback.Command (html, "Edit"));
			string edit = await editing.Content.ReadAsStringAsync ();

			// The name of the TextBox the edit template rendered. It is not guessable - it is composed
			// from the grid, the row, the cell, the DynamicControl and the template - so it is read back
			// out of the markup, which is also what proves the template really rendered it.
			// Attribute order is the control's business, not the test's, so match the whole tag and pull
			// the name out of it rather than assuming name= comes before value=.
			string field = System.Text.RegularExpressions.Regex
				.Matches (edit, "<input[^>]*value=\"Anvil\"[^>]*>")
				.Select (m => System.Text.RegularExpressions.Regex.Match (m.Value, "name=\"(?<name>[^\"]*)\""))
				.Where (m => m.Success)
				.Select (m => m.Groups ["name"].Value)
				.FirstOrDefault ();

			Assert.False (String.IsNullOrEmpty (field), "no edit control was rendered holding the value");

			HttpResponseMessage updated = await client.PostAsync (
				"/Products/List.aspx", Postback.Command (edit, "Update", (field, "Sledgehammer")));
			string after = await updated.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, updated.StatusCode);
			Assert.Contains ("Sledgehammer", after);
			Assert.DoesNotContain ("Anvil", after);

			// Put it back: the sample's context is static for the process, so a test that renames a row
			// and leaves it renamed makes every later assertion order-dependent.
			string restoreSource = await client.GetStringAsync ("/Products/List.aspx");
			HttpResponseMessage restoreEdit = await client.PostAsync (
				"/Products/List.aspx", Postback.Command (restoreSource, "Edit"));
			string restoreForm = await restoreEdit.Content.ReadAsStringAsync ();

			string restoreField = System.Text.RegularExpressions.Regex
				.Matches (restoreForm, "<input[^>]*value=\"Sledgehammer\"[^>]*>")
				.Select (m => System.Text.RegularExpressions.Regex.Match (m.Value, "name=\"(?<name>[^\"]*)\""))
				.Where (m => m.Success)
				.Select (m => m.Groups ["name"].Value)
				.FirstOrDefault ();

			await client.PostAsync (
				"/Products/List.aspx", Postback.Command (restoreForm, "Update", (restoreField, "Anvil")));
		}

		[Fact]
		public async Task Cancelling_an_edit_leaves_the_row_alone ()
		{
			using HttpClient client = fixture.CreateClient ();
			string html = await client.GetStringAsync ("/Products/List.aspx");

			HttpResponseMessage editing = await client.PostAsync (
				"/Products/List.aspx", Postback.Command (html, "Edit"));
			string edit = await editing.Content.ReadAsStringAsync ();

			HttpResponseMessage cancelled = await client.PostAsync (
				"/Products/List.aspx", Postback.Command (edit, "Cancel"));
			string after = await cancelled.Content.ReadAsStringAsync ();

			Assert.Equal (HttpStatusCode.OK, cancelled.StatusCode);
			Assert.Contains ("Anvil", after);
			Assert.DoesNotContain ("type=\"text\"", after);
		}
	}
}
