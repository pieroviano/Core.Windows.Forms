//
// An opt-in IStateObjectSerializer built on System.Text.Json.
//
// This is NOT installed by default and deliberately so - see IStateObjectSerializer for why a silent
// substitute for BinaryFormatter would be worse than an error. Applications that put their own DTOs
// in view state or session can opt in:
//
//     app.UseWebForms (o => o.StateSerializer = new JsonStateObjectSerializer ());
//
// What it handles: types with a public parameterless constructor and public read/write properties -
// which is what a DTO in view state normally is.
//
// What it does NOT do, and BinaryFormatter did:
//   * private fields are not serialized (only public properties);
//   * object identity is not preserved - two references to the same instance come back as two
//     instances, and cycles throw rather than round-trip;
//   * polymorphism is limited to the declared type name written in the stream.
//
// Those are stated rather than papered over: if any of them matters, store a type the formatter
// handles natively instead.
//
// Wire format, self-delimiting because these streams are shared:
//     [7-bit encoded length][UTF-8 assembly-qualified type name][7-bit encoded length][UTF-8 JSON]
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace System.Web
{
	public sealed class JsonStateObjectSerializer : IStateObjectSerializer
	{
		readonly JsonSerializerOptions options;

		public JsonStateObjectSerializer ()
			: this (null)
		{
		}

		public JsonStateObjectSerializer (JsonSerializerOptions options)
		{
			this.options = options ?? new JsonSerializerOptions {
				// A cycle would otherwise throw deep inside the writer with a confusing message.
				ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
				IncludeFields = true,
			};
		}

		public bool CanSerialize (Type type)
		{
			if (type == null)
				return false;
			// Delegates and pointers have no meaningful JSON form; reject rather than emit "{}".
			if (typeof (Delegate).IsAssignableFrom (type) || type.IsPointer)
				return false;
			return true;
		}

		public void Serialize (Stream stream, object graph)
		{
			if (stream == null)
				throw new ArgumentNullException ("stream");
			if (graph == null)
				throw new ArgumentNullException ("graph");

			Type type = graph.GetType ();
			byte [] typeName = Encoding.UTF8.GetBytes (type.AssemblyQualifiedName);
			byte [] payload = JsonSerializer.SerializeToUtf8Bytes (graph, type, options);

			WriteLengthPrefixed (stream, typeName);
			WriteLengthPrefixed (stream, payload);
		}

		public object Deserialize (Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException ("stream");

			byte [] typeName = ReadLengthPrefixed (stream);
			byte [] payload = ReadLengthPrefixed (stream);

			string name = Encoding.UTF8.GetString (typeName);
			Type type = Type.GetType (name, false);
			if (type == null)
				throw new InvalidOperationException (String.Format (
					"Serialized state names type '{0}', which cannot be loaded. The assembly it came " +
					"from is probably missing or was renamed.", name));

			return JsonSerializer.Deserialize (payload, type, options);
		}

		static void WriteLengthPrefixed (Stream stream, byte [] data)
		{
			Write7BitEncodedInt (stream, data.Length);
			stream.Write (data, 0, data.Length);
		}

		static byte [] ReadLengthPrefixed (Stream stream)
		{
			int length = Read7BitEncodedInt (stream);
			byte [] data = new byte [length];
			int offset = 0;
			while (offset < length) {
				int read = stream.Read (data, offset, length - offset);
				if (read <= 0)
					throw new EndOfStreamException ("Corrupt serialized state: stream ended mid-value.");
				offset += read;
			}
			return data;
		}

		static void Write7BitEncodedInt (Stream stream, int value)
		{
			uint v = (uint) value;
			while (v >= 0x80) {
				stream.WriteByte ((byte) (v | 0x80));
				v >>= 7;
			}
			stream.WriteByte ((byte) v);
		}

		static int Read7BitEncodedInt (Stream stream)
		{
			int count = 0, shift = 0;
			while (true) {
				if (shift == 5 * 7)
					throw new InvalidOperationException ("Corrupt serialized state: bad 7-bit encoded length.");
				int b = stream.ReadByte ();
				if (b < 0)
					throw new EndOfStreamException ("Corrupt serialized state: stream ended in a length prefix.");
				count |= (b & 0x7F) << shift;
				shift += 7;
				if ((b & 0x80) == 0)
					return count;
			}
		}
	}
}
