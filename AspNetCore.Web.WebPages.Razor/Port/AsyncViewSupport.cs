//
// @await in Razor views.
//
// Two things have to be true for "@await Something ()" to work, and neither was:
//
//   1. the PARSER has to keep reading past "await". Razor v2's implicit-expression parser accepts one
//      identifier and then only method calls and indexers, so "@await Foo ()" parsed as the expression
//      "await" followed by the literal text " Foo ()". That half is a patch to CSharpCodeParser
//      (see Tools/port-patches.txt) - it is three lines and belongs where the parsing happens.
//
//   2. the generated METHOD has to be able to contain an await. Razor emits
//      "public override void Execute ()", and you cannot await inside a void non-async method. That
//      is this file.
//
// The transform
// -------------
// The Execute method Razor built is renamed and marked async, and a small synchronous Execute () is
// added that runs it:
//
//     public async System.Threading.Tasks.Task __ExecuteAsync () { ...the view... }
//     public override void Execute () { __ExecuteAsync ().GetAwaiter ().GetResult (); }
//
// __ExecuteAsync is deliberately NOT an override. Making it one would mean adding a virtual to
// WebPageExecutingBase, i.e. editing upstream's class hierarchy and every base class that derives
// from it; as a plain public method it needs nothing from the base at all, and Execute () - which IS
// the abstract member - is still implemented exactly once.
//
// Sync-over-async, honestly
// -------------------------
// Execute () blocks on the task. That is normally a deadlock hazard, and here it is not: the classic
// deadlock needs a SynchronizationContext that marshals the continuation back to the blocked thread,
// and ASP.NET Core does not install one. What remains is a real cost - the request's thread is held
// while the view's awaits complete, so a view awaiting slow I/O occupies a thread-pool thread it
// would not occupy under ASP.NET Core's own view engine.
//
// The alternative was making the whole render path async, which the port's pipeline is explicitly not
// (LIMITATIONS §5: synchronous inside, async at the edge). Async views are worth having on their own
// terms - they let a view call an async API without the caller restructuring - and this is the honest
// price.
//
// Cost when unused: none. Views with no await are left exactly as Razor generated them, so the common
// case gains neither a state machine nor a blocking call.
//

using System;
using System.CodeDom;
using System.Web.Razor.Generator;

namespace System.Web.WebPages.Razor
{
	/// <summary>
	/// Rewrites a generated view's Execute method so it can contain <c>await</c>.
	/// </summary>
	public static class AsyncViewSupport
	{
		/// <summary>Name of the generated async method. Visible in stack traces, hence the __ prefix.</summary>
		public const string AsyncExecuteMethodName = "__ExecuteAsync";

		/// <summary>
		/// CodeDom has no notion of "async", so the modifier is smuggled in as part of the return type
		/// string. CSharpCodeProvider writes a return type verbatim, which makes this reliable rather
		/// than merely lucky - but it is the reason the type is spelled out in full here.
		/// </summary>
		const string AsyncTaskReturnType = "async System.Threading.Tasks.Task";

		public static void Apply (CodeGeneratorContext context)
		{
			if (context == null || context.TargetMethod == null)
				return;

			CodeMemberMethod execute = context.TargetMethod;

			// Only views that actually await pay for this. Razor emits view code as snippets, so the
			// generated text is the only place the await can be seen at this stage - there is no
			// CodeDom node for it.
			if (!ContainsAwait (execute))
				return;

			var bridge = new CodeMemberMethod {
				Name = execute.Name,
				Attributes = execute.Attributes,
				ReturnType = new CodeTypeReference (typeof (void)),
			};

			bridge.Statements.Add (new CodeSnippetStatement (
				"            " + AsyncExecuteMethodName + " ().GetAwaiter ().GetResult ();"));

			// Renamed and de-virtualised in place, so every statement, line pragma and code mapping
			// Razor built stays attached to it - rebuilding the method would lose the mappings and
			// with them every stack trace that points into the .cshtml.
			execute.Name = AsyncExecuteMethodName;
			execute.Attributes = MemberAttributes.Public | MemberAttributes.Final;
			execute.ReturnType = new CodeTypeReference (AsyncTaskReturnType);

			context.GeneratedClass.Members.Add (bridge);
		}

		static bool ContainsAwait (CodeMemberMethod method)
		{
			foreach (CodeStatement statement in method.Statements) {
				var snippet = statement as CodeSnippetStatement;
				if (snippet != null && MentionsAwait (snippet.Value))
					return true;

				var expression = statement as CodeExpressionStatement;
				if (expression != null && MentionsAwait (Flatten (expression.Expression)))
					return true;

				var assign = statement as CodeAssignStatement;
				if (assign != null && MentionsAwait (Flatten (assign.Right)))
					return true;

				var variable = statement as CodeVariableDeclarationStatement;
				if (variable != null && MentionsAwait (Flatten (variable.InitExpression)))
					return true;
			}

			return false;
		}

		static string Flatten (CodeExpression expression)
		{
			var snippet = expression as CodeSnippetExpression;
			if (snippet != null)
				return snippet.Value;

			var invoke = expression as CodeMethodInvokeExpression;
			if (invoke != null) {
				var text = new System.Text.StringBuilder ();
				foreach (CodeExpression argument in invoke.Parameters)
					text.Append (Flatten (argument)).Append (' ');

				return text.ToString ();
			}

			return null;
		}

		/// <summary>
		/// Whether the text contains <c>await</c> as a WORD. A substring test would fire on "awaited",
		/// on a string literal containing the word, and on any identifier ending in it - turning an
		/// ordinary view into an async one for no reason.
		/// </summary>
		static bool MentionsAwait (string text)
		{
			if (String.IsNullOrEmpty (text))
				return false;

			int index = 0;
			while ((index = text.IndexOf ("await", index, StringComparison.Ordinal)) >= 0) {
				bool startsWord = index == 0 || !IsIdentifierChar (text [index - 1]);
				int after = index + "await".Length;
				bool endsWord = after >= text.Length || !IsIdentifierChar (text [after]);

				if (startsWord && endsWord)
					return true;

				index = after;
			}

			return false;
		}

		static bool IsIdentifierChar (char c)
		{
			return Char.IsLetterOrDigit (c) || c == '_';
		}
	}
}
