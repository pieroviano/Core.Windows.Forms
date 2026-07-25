//
// Replaces BinaryFormatter in the state-serialization paths (plan P6).
//
// BinaryFormatter is disabled by default on .NET 8 and removed in .NET 9, so every use throws
// NotSupportedException. Upstream reaches for it in two quite different situations, and they deserve
// different answers:
//
//   1. ObjectStateFormatter.SingleRankArrayFormatter, for arrays whose element type is primitive
//      (int[], bool[], double[], ...). Non-primitive arrays already take a native recursive path;
//      only the primitive case detoured through BinaryFormatter. This is trivially representable
//      with BinaryWriter, so PrimitiveArray below serialises it natively and NOTHING is lost. That
//      matters: this path is not exotic - ClientScriptManager's event-validation state is a
//      primitive array, so every page containing a <form runat="server"> hit it.
//
//   2. ObjectStateFormatter.BinaryObjectFormatter and the session/output-cache helpers, as the
//      catch-all for types the formatter has no specialised writer for. There is no faithful
//      substitute: BinaryFormatter serialised arbitrary object graphs by walking private fields.
//      Guessing (JSON, DataContract) would silently change what round-trips, so these throw with a
//      message naming the offending type and the two real options.
//
// Viewstate is opaque and MAC-protected per application, so changing the encoding in (1) is not a
// compatibility concern - nothing outside this assembly ever reads it.
//

using System;
using System.IO;

namespace System.Web.Util
{
	static class StateSerializer
	{
		//
		// Native encoding for single-rank primitive arrays.
		//
		// Format: [element type code : 1 byte][length : 7-bit encoded int][elements...]
		//
		internal static class PrimitiveArray
		{
			const byte TypeBoolean = 1;
			const byte TypeByte = 2;
			const byte TypeSByte = 3;
			const byte TypeChar = 4;
			const byte TypeInt16 = 5;
			const byte TypeUInt16 = 6;
			const byte TypeInt32 = 7;
			const byte TypeUInt32 = 8;
			const byte TypeInt64 = 9;
			const byte TypeUInt64 = 10;
			const byte TypeSingle = 11;
			const byte TypeDouble = 12;

			public static void Write (BinaryWriter w, Array value)
			{
				if (w == null)
					throw new ArgumentNullException ("w");
				if (value == null)
					throw new ArgumentNullException ("value");

				Type element = value.GetType ().GetElementType ();
				byte code = CodeFor (element);
				w.Write (code);
				WriteLength (w, value.Length);

				switch (code) {
				case TypeBoolean:
					foreach (bool v in (bool []) value) w.Write (v);
					break;
				case TypeByte:
					w.Write ((byte []) value);
					break;
				case TypeSByte:
					foreach (sbyte v in (sbyte []) value) w.Write (v);
					break;
				case TypeChar:
					foreach (char v in (char []) value) w.Write (v);
					break;
				case TypeInt16:
					foreach (short v in (short []) value) w.Write (v);
					break;
				case TypeUInt16:
					foreach (ushort v in (ushort []) value) w.Write (v);
					break;
				case TypeInt32:
					foreach (int v in (int []) value) w.Write (v);
					break;
				case TypeUInt32:
					foreach (uint v in (uint []) value) w.Write (v);
					break;
				case TypeInt64:
					foreach (long v in (long []) value) w.Write (v);
					break;
				case TypeUInt64:
					foreach (ulong v in (ulong []) value) w.Write (v);
					break;
				case TypeSingle:
					foreach (float v in (float []) value) w.Write (v);
					break;
				case TypeDouble:
					foreach (double v in (double []) value) w.Write (v);
					break;
				}
			}

			public static object Read (BinaryReader r)
			{
				if (r == null)
					throw new ArgumentNullException ("r");

				byte code = r.ReadByte ();
				int length = ReadLength (r);

				switch (code) {
				case TypeBoolean: {
					bool [] a = new bool [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadBoolean ();
					return a;
				}
				case TypeByte:
					return r.ReadBytes (length);
				case TypeSByte: {
					sbyte [] a = new sbyte [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadSByte ();
					return a;
				}
				case TypeChar: {
					char [] a = new char [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadChar ();
					return a;
				}
				case TypeInt16: {
					short [] a = new short [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadInt16 ();
					return a;
				}
				case TypeUInt16: {
					ushort [] a = new ushort [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadUInt16 ();
					return a;
				}
				case TypeInt32: {
					int [] a = new int [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadInt32 ();
					return a;
				}
				case TypeUInt32: {
					uint [] a = new uint [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadUInt32 ();
					return a;
				}
				case TypeInt64: {
					long [] a = new long [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadInt64 ();
					return a;
				}
				case TypeUInt64: {
					ulong [] a = new ulong [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadUInt64 ();
					return a;
				}
				case TypeSingle: {
					float [] a = new float [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadSingle ();
					return a;
				}
				case TypeDouble: {
					double [] a = new double [length];
					for (int i = 0; i < length; i++) a [i] = r.ReadDouble ();
					return a;
				}
				}

				throw new InvalidOperationException (
					"Corrupt serialized state: unknown primitive array element code " + code + ".");
			}

			static byte CodeFor (Type element)
			{
				if (element == typeof (bool)) return TypeBoolean;
				if (element == typeof (byte)) return TypeByte;
				if (element == typeof (sbyte)) return TypeSByte;
				if (element == typeof (char)) return TypeChar;
				if (element == typeof (short)) return TypeInt16;
				if (element == typeof (ushort)) return TypeUInt16;
				if (element == typeof (int)) return TypeInt32;
				if (element == typeof (uint)) return TypeUInt32;
				if (element == typeof (long)) return TypeInt64;
				if (element == typeof (ulong)) return TypeUInt64;
				if (element == typeof (float)) return TypeSingle;
				if (element == typeof (double)) return TypeDouble;

				// IntPtr/UIntPtr are primitive but cannot meaningfully cross a request boundary.
				throw new NotSupportedException (String.Format (
					"Arrays of '{0}' cannot be stored in view state by this port of System.Web.",
					element));
			}

			// Same 7-bit encoding ObjectStateFormatter uses elsewhere, kept local so this type has
			// no dependency on the formatter's internals.
			static void WriteLength (BinaryWriter w, int value)
			{
				uint v = (uint) value;
				while (v >= 0x80) {
					w.Write ((byte) (v | 0x80));
					v >>= 7;
				}
				w.Write ((byte) v);
			}

			static int ReadLength (BinaryReader r)
			{
				int count = 0, shift = 0;
				byte b;
				do {
					if (shift == 5 * 7)
						throw new InvalidOperationException ("Corrupt serialized state: bad 7-bit encoded length.");
					b = r.ReadByte ();
					count |= (b & 0x7F) << shift;
					shift += 7;
				} while ((b & 0x80) != 0);
				return count;
			}
		}

		//
		// The catch-all that used BinaryFormatter. No serializer is installed by default: see
		// IStateObjectSerializer for why substituting one silently would be worse than failing. An
		// application that needs it opts in, e.g.
		//
		//     app.UseWebForms (o => o.StateSerializer = new JsonStateObjectSerializer ());
		//
		internal static IStateObjectSerializer Serializer { get; set; }

		internal static void SerializeUnsupported (Stream stream, object graph)
		{
			Type type = graph == null ? null : graph.GetType ();
			IStateObjectSerializer serializer = Serializer;

			if (serializer == null || type == null || !serializer.CanSerialize (type))
				throw new NotSupportedException (BuildMessage (type, serializer));

			serializer.Serialize (stream, graph);
		}

		internal static object DeserializeUnsupported (Stream stream)
		{
			IStateObjectSerializer serializer = Serializer;
			if (serializer == null)
				throw new NotSupportedException (BuildMessage (null, null));

			return serializer.Deserialize (stream);
		}

		static string BuildMessage (Type type, IStateObjectSerializer serializer)
		{
			string what = type == null
				? "An object"
				: String.Format ("An object of type '{0}'", type.FullName);

			if (serializer != null)
				return what + " was rejected by the installed IStateObjectSerializer (" +
					serializer.GetType ().FullName + "). Store a type it accepts, or install a " +
					"different serializer.";

			return what + " has no native encoding in this port's state formatters, and BinaryFormatter " +
				"- which upstream fell back to - is disabled on .NET 8 and removed in .NET 9. Either " +
				"store a type the formatter handles natively (primitives, string, DateTime, Pair, " +
				"Triplet, ArrayList, Hashtable, arrays of those, and types with a TypeConverter), or " +
				"opt in to a serializer: app.UseWebForms (o => o.StateSerializer = new JsonStateObjectSerializer ()).";
		}
	}
}
