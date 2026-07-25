//
// An IStateObjectSerializer with BinaryFormatter's SEMANTICS and none of its reach.
//
// Why this exists
// ---------------
// mode="InProc" never wrote a session down, so an application could keep anything in it. Move to
// StateServer or SQLServer and every value has to serialize - and the types that break are exactly the
// ones JSON cannot express: private fields, object cycles, shared references, polymorphic fields
// declared as a base type or object. JsonStateObjectSerializer is safe and readable but silently
// changes all four, which is worse than failing.
//
// So this walks fields the way BinaryFormatter did: every instance field, public and private, inherited
// included; references tracked so a cycle terminates and a shared object stays shared; the concrete
// runtime type recorded so polymorphism survives.
//
// Why it is allow-listed by DEFAULT
// ---------------------------------
// BinaryFormatter was not removed for being slow. It was removed because deserialising an attacker-
// influenced payload can construct arbitrary types and run their code - the "gadget chain" class of
// vulnerability - and session state is exactly such a payload once it lives in Redis or a shared
// database that something else can write to.
//
// Reimplementing the semantics without reimplementing the vulnerability means deciding, on the way IN,
// which types may be constructed. Hence: nothing is deserialized unless its type was allow-listed, and
// the allow-list is populated from the types the application itself serialized plus whatever the caller
// adds. AllowAnyType exists for a single-instance deployment where the store is not shared, and says
// what it costs in its own summary.
//
// What is deliberately NOT supported
// ---------------------------------
//   * [OnDeserializing]/[OnDeserialized] callbacks and ISerializable. Both are hooks for running code
//     during deserialization, which is the thing being defended against. A type needing them is a type
//     to reconsider keeping in Session.
//   * Delegates and events. A serialized delegate is a serialized method pointer; it cannot mean
//     anything in another process, and BinaryFormatter's support for it was a mistake.
//   * Anything the runtime owns - Stream, Socket, HttpContext. These fail with a message naming the
//     field, which is far more useful than a corrupt object on the way back.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace System.Web
{
	/// <summary>
	/// Round-trips arbitrary object graphs - private fields, cycles, shared references and
	/// polymorphism - for out-of-process session state and view state.
	/// </summary>
	/// <remarks>
	/// Opt in from the host:
	/// <code>
	/// app.UseWebForms (o => o.StateSerializer = new ObjectGraphStateSerializer {
	///     AllowedTypes = { typeof (MyApp.Basket), typeof (MyApp.BasketLine) },
	/// });
	/// </code>
	/// </remarks>
	public sealed class ObjectGraphStateSerializer : IStateObjectSerializer
	{
		const byte FormatVersion = 1;

		// Tags. Kept small and explicit rather than derived from an enum, because the on-disk format
		// has to survive a refactor of this file.
		const byte TagNull = 0;
		const byte TagReference = 1;
		const byte TagObject = 2;
		const byte TagArray = 3;
		const byte TagString = 4;
		const byte TagPrimitive = 5;
		const byte TagEnum = 6;

		static readonly ConcurrentDictionary<Type, FieldInfo []> FieldCache =
			new ConcurrentDictionary<Type, FieldInfo []> ();

		/// <summary>
		/// Types that may be reconstructed on the way back. Add every type you keep in Session, plus the
		/// types of its fields; the collection is consulted on DESERIALIZE, which is the direction that
		/// matters for safety.
		/// </summary>
		public ICollection<Type> AllowedTypes { get; } = new HashSet<Type> ();

		/// <summary>
		/// Skip the allow-list entirely.
		/// </summary>
		/// <remarks>
		/// Only safe when the session store cannot be written by anything but this application - a
		/// single instance with an in-memory cache, say. Point it at a shared Redis or an ASPState
		/// database that another system can reach and this is the deserialization vulnerability that
		/// caused BinaryFormatter to be removed from .NET, reintroduced by hand.
		/// </remarks>
		public bool AllowAnyType { get; set; }

		/// <summary>
		/// Namespaces whose types are allowed wholesale, e.g. "MyApp.Models". Convenience for an
		/// application with a lot of DTOs; still far narrower than AllowAnyType.
		/// </summary>
		public ICollection<string> AllowedNamespaces { get; } = new HashSet<string> (StringComparer.Ordinal);

		public bool CanSerialize (Type type)
		{
			// Everything except the things that cannot mean anything in another process. Note this is
			// the OUTBOUND question - the allow-list is enforced on the way back in.
			if (type == null)
				return false;

			return !typeof (Delegate).IsAssignableFrom (type) &&
			       !typeof (Stream).IsAssignableFrom (type) &&
			       !typeof (Type).IsAssignableFrom (type) &&
			       !type.IsPointer &&
			       !type.IsByRef;
		}

		public void Serialize (Stream stream, object graph)
		{
			// leaveOpen: these streams carry other state after this value. Closing the writer would
			// close the stream and truncate everything that follows.
			using (var writer = new BinaryWriter (stream, Encoding.UTF8, leaveOpen: true)) {
				writer.Write (FormatVersion);

				var seen = new Dictionary<object, int> (ReferenceEqualityComparer.Instance);
				WriteValue (writer, graph, seen);
			}
		}

		public object Deserialize (Stream stream)
		{
			using (var reader = new BinaryReader (stream, Encoding.UTF8, leaveOpen: true)) {
				byte version = reader.ReadByte ();
				if (version != FormatVersion)
					throw new SerializationException (
						"Session state was written by a different version of ObjectGraphStateSerializer " +
						"(format " + version + ", expected " + FormatVersion + "). Sessions written before " +
						"an upgrade cannot be read after it; flush the store.");

				var seen = new List<object> ();
				return ReadValue (reader, seen);
			}
		}

		// -----------------------------------------------------------------------------------------
		// Writing
		// -----------------------------------------------------------------------------------------

		void WriteValue (BinaryWriter writer, object value, Dictionary<object, int> seen)
		{
			if (value == null) {
				writer.Write (TagNull);
				return;
			}

			Type type = value.GetType ();

			if (type == typeof (string)) {
				writer.Write (TagString);
				writer.Write ((string) value);
				return;
			}

			if (type.IsEnum) {
				writer.Write (TagEnum);
				writer.Write (type.AssemblyQualifiedName);
				WritePrimitive (writer, Convert.ChangeType (value, Enum.GetUnderlyingType (type)));
				return;
			}

			if (IsPrimitiveLike (type)) {
				writer.Write (TagPrimitive);
				WritePrimitive (writer, value);
				return;
			}

			// Reference tracking. This is what makes a cycle terminate and a shared object stay shared -
			// two fields pointing at one instance must still point at one instance afterwards, or a
			// "parent" back-reference silently becomes a second parent.
			int id;
			if (seen.TryGetValue (value, out id)) {
				writer.Write (TagReference);
				Write7Bit (writer, id);
				return;
			}

			id = seen.Count;
			seen [value] = id;

			if (type.IsArray) {
				var array = (Array) value;
				if (array.Rank != 1)
					throw new SerializationException (
						"Only single-dimension arrays are supported; " + type.FullName + " has rank " +
						array.Rank + ".");

				writer.Write (TagArray);
				writer.Write (type.GetElementType ().AssemblyQualifiedName);
				Write7Bit (writer, array.Length);

				foreach (object element in array)
					WriteValue (writer, element, seen);

				return;
			}

			if (!CanSerialize (type))
				throw new SerializationException (
					"A value of type " + type.FullName + " cannot be stored in out-of-process state. " +
					"Delegates, streams and Type objects have no meaning in another process. Remove it " +
					"from Session, or hold something that identifies it instead.");

			writer.Write (TagObject);
			writer.Write (type.AssemblyQualifiedName);

			FieldInfo [] fields = FieldsOf (type);
			Write7Bit (writer, fields.Length);

			foreach (FieldInfo field in fields) {
				writer.Write (field.Name);

				try {
					WriteValue (writer, field.GetValue (value), seen);
				} catch (SerializationException e) {
					// Rethrown with the path, because "a Socket cannot be serialized" is useless without
					// knowing which field of which object held it.
					throw new SerializationException (
						type.FullName + "." + field.Name + ": " + e.Message, e);
				}
			}
		}

		static void WritePrimitive (BinaryWriter writer, object value)
		{
			switch (value) {
			case bool v: writer.Write ((byte) 1); writer.Write (v); break;
			case byte v: writer.Write ((byte) 2); writer.Write (v); break;
			case sbyte v: writer.Write ((byte) 3); writer.Write (v); break;
			case char v: writer.Write ((byte) 4); writer.Write (v); break;
			case short v: writer.Write ((byte) 5); writer.Write (v); break;
			case ushort v: writer.Write ((byte) 6); writer.Write (v); break;
			case int v: writer.Write ((byte) 7); writer.Write (v); break;
			case uint v: writer.Write ((byte) 8); writer.Write (v); break;
			case long v: writer.Write ((byte) 9); writer.Write (v); break;
			case ulong v: writer.Write ((byte) 10); writer.Write (v); break;
			case float v: writer.Write ((byte) 11); writer.Write (v); break;
			case double v: writer.Write ((byte) 12); writer.Write (v); break;
			case decimal v: writer.Write ((byte) 13); writer.Write (v); break;
			case DateTime v: writer.Write ((byte) 14); writer.Write (v.Ticks); writer.Write ((byte) v.Kind); break;
			case TimeSpan v: writer.Write ((byte) 15); writer.Write (v.Ticks); break;
			case Guid v: writer.Write ((byte) 16); writer.Write (v.ToByteArray ()); break;
			case DateTimeOffset v:
				writer.Write ((byte) 17);
				writer.Write (v.Ticks);
				writer.Write (v.Offset.Ticks);
				break;
			default:
				throw new SerializationException ("Unsupported primitive " + value.GetType ().FullName + ".");
			}
		}

		// -----------------------------------------------------------------------------------------
		// Reading
		// -----------------------------------------------------------------------------------------

		object ReadValue (BinaryReader reader, List<object> seen)
		{
			byte tag = reader.ReadByte ();

			switch (tag) {
			case TagNull:
				return null;

			case TagString:
				return reader.ReadString ();

			case TagPrimitive:
				return ReadPrimitive (reader);

			case TagEnum: {
				Type type = ResolveType (reader.ReadString ());
				object raw = ReadPrimitive (reader);
				return Enum.ToObject (type, raw);
			}

			case TagReference: {
				int id = Read7Bit (reader);
				if (id < 0 || id >= seen.Count)
					throw new SerializationException ("Corrupt state: reference to object " + id +
									  " which has not been read.");
				return seen [id];
			}

			case TagArray: {
				Type element = ResolveType (reader.ReadString ());
				int length = Read7Bit (reader);

				Array array = Array.CreateInstance (element, length);

				// Registered BEFORE the elements are read, so an array that contains itself - or an
				// element whose field points back at the array - resolves instead of recursing forever.
				seen.Add (array);

				for (int i = 0; i < length; i++)
					array.SetValue (ReadValue (reader, seen), i);

				return array;
			}

			case TagObject: {
				Type type = ResolveType (reader.ReadString ());

				// FormatterServices.GetUninitializedObject, exactly as BinaryFormatter did: no
				// constructor runs, so a type with no parameterless constructor still round-trips and
				// no constructor side effect happens twice.
				object instance = RuntimeHelpersGetUninitializedObject (type);
				seen.Add (instance);

				int count = Read7Bit (reader);
				var fields = new Dictionary<string, FieldInfo> (StringComparer.Ordinal);
				foreach (FieldInfo field in FieldsOf (type))
					fields [field.Name] = field;

				for (int i = 0; i < count; i++) {
					string name = reader.ReadString ();
					object value = ReadValue (reader, seen);

					FieldInfo field;
					if (fields.TryGetValue (name, out field))
						field.SetValue (instance, value);

					// A field present in the payload but gone from the type is DROPPED, not an error:
					// that is what happens when a class loses a member between deployments, and taking
					// out every live session for it would be worse than losing one value.
				}

				return instance;
			}

			default:
				throw new SerializationException ("Corrupt state: unknown tag " + tag + ".");
			}
		}

		static object ReadPrimitive (BinaryReader reader)
		{
			byte code = reader.ReadByte ();

			switch (code) {
			case 1: return reader.ReadBoolean ();
			case 2: return reader.ReadByte ();
			case 3: return reader.ReadSByte ();
			case 4: return reader.ReadChar ();
			case 5: return reader.ReadInt16 ();
			case 6: return reader.ReadUInt16 ();
			case 7: return reader.ReadInt32 ();
			case 8: return reader.ReadUInt32 ();
			case 9: return reader.ReadInt64 ();
			case 10: return reader.ReadUInt64 ();
			case 11: return reader.ReadSingle ();
			case 12: return reader.ReadDouble ();
			case 13: return reader.ReadDecimal ();
			case 14: return new DateTime (reader.ReadInt64 (), (DateTimeKind) reader.ReadByte ());
			case 15: return new TimeSpan (reader.ReadInt64 ());
			case 16: return new Guid (reader.ReadBytes (16));
			case 17: {
				long ticks = reader.ReadInt64 ();
				long offset = reader.ReadInt64 ();
				return new DateTimeOffset (ticks, new TimeSpan (offset));
			}
			default:
				throw new SerializationException ("Corrupt state: unknown primitive code " + code + ".");
			}
		}

		/// <summary>
		/// Resolves a type name from the payload - and refuses anything not allowed.
		/// </summary>
		/// <remarks>
		/// This method is the entire security boundary. Everything else here is bookkeeping; this is
		/// the line where an attacker-influenced payload would otherwise get to name a type and have it
		/// constructed.
		/// </remarks>
		Type ResolveType (string assemblyQualifiedName)
		{
			Type type = Type.GetType (assemblyQualifiedName, throwOnError: false);

			if (type == null)
				throw new SerializationException (
					"Session state names the type '" + assemblyQualifiedName + "', which this application " +
					"cannot load. That usually means the session was written by a different build.");

			if (AllowAnyType)
				return type;

			EnsureAllowed (type);
			return type;
		}

		/// <summary>
		/// Checks a type and everything reachable through its name - element type, nullable underlying
		/// type, and every generic argument.
		/// </summary>
		/// <remarks>
		/// The generic-argument walk is not decoration. "List`1[[Evil, SomeAssembly]]" is ONE type name,
		/// and Type.GetType resolves the whole thing in one call - so checking only the outer List would
		/// let an attacker name any type they liked as its argument and have it constructed when the
		/// elements were read. Every part of the name has to be checked, not just the head.
		/// </remarks>
		void EnsureAllowed (Type type)
		{
			Type check = type;

			// Arrays and nullables are allowed exactly when their element type is: an int[] is no more
			// dangerous than an int, and requiring both to be listed is a footgun with no benefit.
			while (check.IsArray)
				check = check.GetElementType ();

			Type nullable = Nullable.GetUnderlyingType (check);
			if (nullable != null)
				check = nullable;

			if (check.IsGenericType) {
				foreach (Type argument in check.GetGenericArguments ())
					EnsureAllowed (argument);

				// The open definition is what gets constructed; its arguments were just checked.
				check = check.GetGenericTypeDefinition ();
			}

			if (IsAlwaysAllowed (check) || AllowedTypes.Contains (check))
				return;

			if (check.Namespace != null && AllowedNamespaces.Contains (check.Namespace))
				return;

			throw new SerializationException (
				"Refusing to deserialize '" + check.FullName + "': it is not on the allow-list. This is " +
				"the check that keeps out-of-process session state from becoming the deserialization " +
				"vulnerability BinaryFormatter was removed for. Add it:" + Environment.NewLine +
				"    o.StateSerializer = new ObjectGraphStateSerializer { AllowedTypes = { typeof (" +
				check.Name + ") } };" + Environment.NewLine +
				"or allow its namespace with AllowedNamespaces, or set AllowAnyType if the store cannot " +
				"be written by anything but this application.");
		}

		/// <summary>
		/// Types that can never be a gadget: they have no fields to populate and no behaviour to
		/// trigger. Allowing them unconditionally keeps the allow-list about the application's own
		/// model rather than about int and string.
		/// </summary>
		static bool IsAlwaysAllowed (Type type)
		{
			if (type.IsPrimitive || type.IsEnum ||
			    type == typeof (string) || type == typeof (decimal) || type == typeof (DateTime) ||
			    type == typeof (TimeSpan) || type == typeof (Guid) || type == typeof (DateTimeOffset) ||
			    type == typeof (object))
				return true;

			// The BCL collections, which are what a model class actually holds. Safe to construct: they
			// carry no behaviour of their own, and every element they hold has already been checked
			// separately by EnsureAllowed's generic-argument walk on the way in.
			//
			// Without this the allow-list would be unusable - every class with a List<T> would need
			// List<T> listed as well as T, and an allow-list nobody keeps accurate protects nothing.
			return AlwaysAllowedCollections.Contains (type);
		}

		static readonly HashSet<Type> AlwaysAllowedCollections = new HashSet<Type> {
			typeof (List<>), typeof (Dictionary<,>), typeof (HashSet<>), typeof (Queue<>),
			typeof (Stack<>), typeof (LinkedList<>), typeof (SortedList<,>), typeof (SortedDictionary<,>),
			typeof (SortedSet<>), typeof (KeyValuePair<,>), typeof (Nullable<>),
			typeof (System.Collections.ArrayList), typeof (System.Collections.Hashtable),
			typeof (Tuple<>), typeof (Tuple<,>), typeof (Tuple<,,>), typeof (Tuple<,,,>),
			typeof (ValueTuple<>), typeof (ValueTuple<,>), typeof (ValueTuple<,,>), typeof (ValueTuple<,,,>),
		};

		// -----------------------------------------------------------------------------------------

		/// <summary>
		/// Every instance field, declared and inherited, in a stable order.
		/// </summary>
		/// <remarks>
		/// Walking the hierarchy explicitly rather than using BindingFlags.FlattenHierarchy, because
		/// that flag does not return PRIVATE fields of base classes - and a private base field is
		/// exactly the state a property would be hiding. Ordered by name within each level so the
		/// payload does not depend on reflection's ordering, which is not guaranteed stable.
		/// </remarks>
		static FieldInfo [] FieldsOf (Type type)
		{
			return FieldCache.GetOrAdd (type, t => {
				var fields = new List<FieldInfo> ();

				for (Type current = t; current != null && current != typeof (object); current = current.BaseType) {
					FieldInfo [] declared = current.GetFields (
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
						BindingFlags.DeclaredOnly);

					Array.Sort (declared, (a, b) => String.CompareOrdinal (a.Name, b.Name));

					foreach (FieldInfo field in declared) {
						// NotSerialized is honoured, as it always was - it is how a type says "this one
						// is a cache" or "this one is a handle".
						if (!field.IsNotSerialized && !field.IsStatic && !field.IsLiteral)
							fields.Add (field);
					}
				}

				return fields.ToArray ();
			});
		}

		static bool IsPrimitiveLike (Type type)
		{
			return type.IsPrimitive || type == typeof (decimal) || type == typeof (DateTime) ||
			       type == typeof (TimeSpan) || type == typeof (Guid) || type == typeof (DateTimeOffset);
		}

		static object RuntimeHelpersGetUninitializedObject (Type type)
		{
			try {
				return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject (type);
			} catch (Exception e) {
				throw new SerializationException (
					"Could not create an instance of " + type.FullName + " without running a constructor: " +
					e.Message, e);
			}
		}

		static void Write7Bit (BinaryWriter writer, int value)
		{
			uint v = (uint) value;
			while (v >= 0x80) {
				writer.Write ((byte) (v | 0x80));
				v >>= 7;
			}

			writer.Write ((byte) v);
		}

		static int Read7Bit (BinaryReader reader)
		{
			int result = 0, shift = 0;

			while (true) {
				byte b = reader.ReadByte ();
				result |= (b & 0x7F) << shift;

				if ((b & 0x80) == 0)
					return result;

				shift += 7;
				if (shift > 35)
					throw new SerializationException ("Corrupt state: bad 7-bit encoded length.");
			}
		}

		/// <summary>
		/// Identity comparison for the seen-set. A type that overrides Equals - a value object, say -
		/// would otherwise collapse two distinct instances into one shared reference on the way back.
		/// </summary>
		sealed class ReferenceEqualityComparer : IEqualityComparer<object>
		{
			public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer ();

			public new bool Equals (object x, object y)
			{
				return ReferenceEquals (x, y);
			}

			public int GetHashCode (object obj)
			{
				return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode (obj);
			}
		}
	}
}
