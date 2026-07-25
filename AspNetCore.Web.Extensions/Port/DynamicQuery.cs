//
// The dynamic-query syntax LinqDataSource markup is written in.
//
// "CategoryId == @Category && Price > 10" and "Name DESC, Price" are strings in an .aspx file, and they
// have to become expression trees a queryable provider can translate. Upstream leaned on
// System.Linq.Dynamic for this; it is not in the framework, so it is here.
//
// Two decisions worth stating, because both are security-relevant:
//
//   * Only PROPERTY and FIELD access is parseable. There is no method-call syntax, no indexer, no
//     "new" except the projection form. An expression language in markup that can call methods is one
//     an attacker reaches through anything that writes markup.
//   * Parameter values NEVER become part of the parsed text. "@Category" resolves to a captured
//     constant in the expression tree, so a value containing "|| 1 == 1" is a value, not syntax. This
//     is the same reason a SQL parameter is not string concatenation.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace System.Web.UI.WebControls
{
	static class DynamicQuery
	{
		public static IQueryable Where (IQueryable source, string predicate, IDictionary<string, object> values)
		{
			if (String.IsNullOrWhiteSpace (predicate))
				return source;

			ParameterExpression parameter = Expression.Parameter (source.ElementType, "it");
			Expression body = new Parser (predicate, parameter, values).ParseExpression ();

			if (body.Type != typeof (bool))
				throw new InvalidOperationException (
					"The Where expression \"" + predicate + "\" evaluates to " + body.Type.Name +
					", not a boolean.");

			LambdaExpression lambda = Expression.Lambda (body, parameter);

			return source.Provider.CreateQuery (
				Expression.Call (typeof (Queryable), "Where", new [] { source.ElementType },
						 source.Expression, Expression.Quote (lambda)));
		}

		/// <summary>
		/// "Name DESC, Price" - the syntax a GridView's SortExpression also produces, which is why one
		/// parser serves both.
		/// </summary>
		public static IQueryable OrderBy (IQueryable source, string ordering)
		{
			if (String.IsNullOrWhiteSpace (ordering))
				return source;

			bool first = true;

			foreach (string term in ordering.Split (',')) {
				string[] parts = term.Trim ().Split (new [] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length == 0)
					continue;

				bool descending = parts.Length > 1 &&
					(parts [1].Equals ("DESC", StringComparison.OrdinalIgnoreCase) ||
					 parts [1].Equals ("DESCENDING", StringComparison.OrdinalIgnoreCase));

				ParameterExpression parameter = Expression.Parameter (source.ElementType, "it");
				Expression member = Parser.ResolveMemberPath (parameter, parts [0]);
				LambdaExpression selector = Expression.Lambda (member, parameter);

				string method = first
					? (descending ? "OrderByDescending" : "OrderBy")
					: (descending ? "ThenByDescending" : "ThenBy");

				source = source.Provider.CreateQuery (
					Expression.Call (typeof (Queryable), method,
							 new [] { source.ElementType, member.Type },
							 source.Expression, Expression.Quote (selector)));
				first = false;
			}

			return source;
		}

		/// <summary>
		/// Orders by the first property when nothing else has, so Skip/Take is deterministic.
		/// </summary>
		/// <remarks>
		/// An unordered Skip lets the database return any rows it likes for a given page - so page 2
		/// can repeat a row from page 1, intermittently, under load. Arbitrary but stable beats
		/// correct-looking and not.
		/// </remarks>
		public static IQueryable OrderByFirstPropertyIfUnordered (IQueryable source)
		{
			PropertyInfo property = source.ElementType
				.GetProperties (BindingFlags.Public | BindingFlags.Instance)
				.FirstOrDefault (p => p.CanRead && IsComparable (p.PropertyType));

			return property == null ? source : OrderBy (source, property.Name);
		}

		/// <summary>Projection: "new (Id, Name)", or a single member.</summary>
		public static IQueryable Select (IQueryable source, string selector)
		{
			if (String.IsNullOrWhiteSpace (selector))
				return source;

			string trimmed = selector.Trim ();

			if (!trimmed.StartsWith ("new", StringComparison.OrdinalIgnoreCase)) {
				ParameterExpression single = Expression.Parameter (source.ElementType, "it");
				Expression member = Parser.ResolveMemberPath (single, trimmed);

				return source.Provider.CreateQuery (
					Expression.Call (typeof (Queryable), "Select",
							 new [] { source.ElementType, member.Type },
							 source.Expression, Expression.Quote (Expression.Lambda (member, single))));
			}

			// "new (Id, Name)" -> an anonymous-like projection. A real anonymous type cannot be built at
			// runtime, so this yields a Dictionary<string,object> per row, which is what the data-bound
			// controls read through ICustomTypeDescriptor anyway.
			int open = trimmed.IndexOf ('(');
			int close = trimmed.LastIndexOf (')');
			if (open < 0 || close < open)
				throw new InvalidOperationException (
					"Malformed Select expression \"" + selector + "\". Expected \"new (A, B)\".");

			string [] names = trimmed.Substring (open + 1, close - open - 1)
				.Split (',').Select (n => n.Trim ()).Where (n => n.Length > 0).ToArray ();

			ParameterExpression parameter = Expression.Parameter (source.ElementType, "it");

			MethodInfo add = typeof (Dictionary<string, object>).GetMethod ("Add");
			var initialisers = names.Select (n => Expression.ElementInit (
				add,
				Expression.Constant (LastSegment (n)),
				Expression.Convert (Parser.ResolveMemberPath (parameter, n), typeof (object))));

			Expression body = Expression.ListInit (
				Expression.New (typeof (Dictionary<string, object>)), initialisers);

			return source.Provider.CreateQuery (
				Expression.Call (typeof (Queryable), "Select",
						 new [] { source.ElementType, typeof (Dictionary<string, object>) },
						 source.Expression,
						 Expression.Quote (Expression.Lambda (body, parameter))));
		}

		static string LastSegment (string path)
		{
			int dot = path.LastIndexOf ('.');
			return dot < 0 ? path : path.Substring (dot + 1);
		}

		static bool IsComparable (Type type)
		{
			Type underlying = Nullable.GetUnderlyingType (type) ?? type;
			return underlying.IsPrimitive || underlying == typeof (string) || underlying == typeof (decimal) ||
			       underlying == typeof (DateTime) || underlying == typeof (Guid);
		}

		/// <summary>
		/// A deliberately small recursive-descent parser: || &amp;&amp; == != &lt; &lt;= &gt; &gt;= + - * / ! ( ),
		/// member paths, @parameters and literals. Nothing else - see the file header.
		/// </summary>
		sealed class Parser
		{
			readonly string text;
			readonly ParameterExpression parameter;
			readonly IDictionary<string, object> values;
			int position;

			public Parser (string text, ParameterExpression parameter, IDictionary<string, object> values)
			{
				this.text = text;
				this.parameter = parameter;
				this.values = values ?? new Dictionary<string, object> ();
			}

			public Expression ParseExpression ()
			{
				Expression result = ParseOr ();
				SkipWhitespace ();

				if (position < text.Length)
					throw new InvalidOperationException (
						"Unexpected '" + text [position] + "' at offset " + position + " in \"" + text + "\".");

				return result;
			}

			Expression ParseOr ()
			{
				Expression left = ParseAnd ();

				while (Match ("||") || Match ("or ")) {
					Expression right = ParseAnd ();
					left = Expression.OrElse (ToBool (left), ToBool (right));
				}

				return left;
			}

			Expression ParseAnd ()
			{
				Expression left = ParseComparison ();

				while (Match ("&&") || Match ("and ")) {
					Expression right = ParseComparison ();
					left = Expression.AndAlso (ToBool (left), ToBool (right));
				}

				return left;
			}

			Expression ParseComparison ()
			{
				Expression left = ParseAdditive ();

				while (true) {
					SkipWhitespace ();

					if (Match ("==")) { left = Compare (left, ParseAdditive (), Expression.Equal); continue; }
					if (Match ("!=")) { left = Compare (left, ParseAdditive (), Expression.NotEqual); continue; }
					if (Match ("<=")) { left = Compare (left, ParseAdditive (), Expression.LessThanOrEqual); continue; }
					if (Match (">=")) { left = Compare (left, ParseAdditive (), Expression.GreaterThanOrEqual); continue; }
					if (Match ("<")) { left = Compare (left, ParseAdditive (), Expression.LessThan); continue; }
					if (Match (">")) { left = Compare (left, ParseAdditive (), Expression.GreaterThan); continue; }

					return left;
				}
			}

			Expression ParseAdditive ()
			{
				Expression left = ParseMultiplicative ();

				while (true) {
					SkipWhitespace ();

					if (Match ("+")) { left = Arithmetic (left, ParseMultiplicative (), Expression.Add); continue; }
					if (Match ("-")) { left = Arithmetic (left, ParseMultiplicative (), Expression.Subtract); continue; }

					return left;
				}
			}

			Expression ParseMultiplicative ()
			{
				Expression left = ParseUnary ();

				while (true) {
					SkipWhitespace ();

					if (Match ("*")) { left = Arithmetic (left, ParseUnary (), Expression.Multiply); continue; }
					if (Match ("/")) { left = Arithmetic (left, ParseUnary (), Expression.Divide); continue; }

					return left;
				}
			}

			Expression ParseUnary ()
			{
				SkipWhitespace ();

				if (Match ("!"))
					return Expression.Not (ToBool (ParseUnary ()));

				if (Match ("-"))
					return Expression.Negate (ParseUnary ());

				return ParsePrimary ();
			}

			Expression ParsePrimary ()
			{
				SkipWhitespace ();

				if (position >= text.Length)
					throw new InvalidOperationException ("Unexpected end of expression in \"" + text + "\".");

				if (Match ("(")) {
					Expression inner = ParseOr ();
					SkipWhitespace ();

					if (!Match (")"))
						throw new InvalidOperationException ("Expected ')' in \"" + text + "\".");

					return inner;
				}

				char c = text [position];

				// @parameter. The VALUE is captured as a constant - it never re-enters the parser, so a
				// value containing operators is data, not syntax.
				if (c == '@') {
					position++;
					string name = ReadIdentifier ();

					object value;
					if (!values.TryGetValue (name, out value))
						throw new InvalidOperationException (
							"The expression \"" + text + "\" uses @" + name +
							", but no parameter of that name was supplied.");

					return Expression.Constant (value, value?.GetType () ?? typeof (object));
				}

				if (c == '"' || c == '\'')
					return Expression.Constant (ReadQuoted (c));

				if (Char.IsDigit (c))
					return Expression.Constant (ReadNumber ());

				if (Char.IsLetter (c) || c == '_') {
					string identifier = ReadMemberPath ();

					switch (identifier) {
					case "true": return Expression.Constant (true);
					case "false": return Expression.Constant (false);
					case "null": return Expression.Constant (null);
					default: return ResolveMemberPath (parameter, identifier);
					}
				}

				throw new InvalidOperationException (
					"Unexpected '" + c + "' at offset " + position + " in \"" + text + "\".");
			}

			static Expression ToBool (Expression expression)
			{
				return expression.Type == typeof (bool)
					? expression
					: Expression.Convert (expression, typeof (bool));
			}

			static Expression Compare (Expression left, Expression right,
						   Func<Expression, Expression, BinaryExpression> build)
			{
				Coerce (ref left, ref right);
				return build (left, right);
			}

			static Expression Arithmetic (Expression left, Expression right,
						      Func<Expression, Expression, BinaryExpression> build)
			{
				Coerce (ref left, ref right);
				return build (left, right);
			}

			/// <summary>
			/// Makes both sides the same type. A QueryString parameter arrives as a string and the
			/// property is an int, which is the normal case rather than the exception - so the constant
			/// is converted to the member's type rather than the other way round.
			/// </summary>
			static void Coerce (ref Expression left, ref Expression right)
			{
				if (left.Type == right.Type)
					return;

				Type leftUnderlying = Nullable.GetUnderlyingType (left.Type) ?? left.Type;
				Type rightUnderlying = Nullable.GetUnderlyingType (right.Type) ?? right.Type;

				if (right is ConstantExpression constant) {
					right = Expression.Constant (ChangeType (constant.Value, leftUnderlying), left.Type);
					return;
				}

				if (left is ConstantExpression leftConstant) {
					left = Expression.Constant (ChangeType (leftConstant.Value, rightUnderlying), right.Type);
					return;
				}

				if (left.Type.IsAssignableFrom (right.Type))
					right = Expression.Convert (right, left.Type);
				else
					left = Expression.Convert (left, right.Type);
			}

			static object ChangeType (object value, Type target)
			{
				if (value == null)
					return null;

				if (target.IsInstanceOfType (value))
					return value;

				if (target.IsEnum)
					return value is string s ? Enum.Parse (target, s, true) : Enum.ToObject (target, value);

				if (target == typeof (Guid))
					return Guid.Parse (Convert.ToString (value, CultureInfo.InvariantCulture));

				return Convert.ChangeType (value, target, CultureInfo.InvariantCulture);
			}

			/// <summary>
			/// "Category.Name" against the row parameter. Null-safe on the way down is deliberately NOT
			/// done: a queryable provider translates the member access to a join, and a null check would
			/// change the SQL.
			/// </summary>
			public static Expression ResolveMemberPath (Expression instance, string path)
			{
				foreach (string segment in path.Split ('.')) {
					Type type = instance.Type;

					PropertyInfo property = type.GetProperty (
						segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
					if (property != null) {
						instance = Expression.Property (instance, property);
						continue;
					}

					FieldInfo field = type.GetField (
						segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
					if (field != null) {
						instance = Expression.Field (instance, field);
						continue;
					}

					throw new InvalidOperationException (
						type.FullName + " has no property or field named '" + segment + "'.");
				}

				return instance;
			}

			void SkipWhitespace ()
			{
				while (position < text.Length && Char.IsWhiteSpace (text [position]))
					position++;
			}

			bool Match (string token)
			{
				SkipWhitespace ();

				if (position + token.Length > text.Length)
					return false;

				if (String.CompareOrdinal (text, position, token, 0, token.Length) != 0)
					return false;

				position += token.Length;
				return true;
			}

			string ReadIdentifier ()
			{
				int start = position;
				while (position < text.Length && (Char.IsLetterOrDigit (text [position]) || text [position] == '_'))
					position++;

				if (start == position)
					throw new InvalidOperationException (
						"Expected a name at offset " + start + " in \"" + text + "\".");

				return text.Substring (start, position - start);
			}

			string ReadMemberPath ()
			{
				int start = position;
				while (position < text.Length &&
				       (Char.IsLetterOrDigit (text [position]) || text [position] == '_' || text [position] == '.'))
					position++;

				return text.Substring (start, position - start);
			}

			string ReadQuoted (char quote)
			{
				position++;   // opening quote
				var value = new StringBuilder ();

				while (position < text.Length && text [position] != quote) {
					if (text [position] == '\\' && position + 1 < text.Length)
						position++;

					value.Append (text [position++]);
				}

				if (position >= text.Length)
					throw new InvalidOperationException ("Unterminated string in \"" + text + "\".");

				position++;   // closing quote
				return value.ToString ();
			}

			object ReadNumber ()
			{
				int start = position;
				bool real = false;

				while (position < text.Length && (Char.IsDigit (text [position]) || text [position] == '.')) {
					if (text [position] == '.')
						real = true;

					position++;
				}

				string literal = text.Substring (start, position - start);

				return real
					? (object) Double.Parse (literal, CultureInfo.InvariantCulture)
					: Int32.Parse (literal, CultureInfo.InvariantCulture);
			}
		}
	}
}
