//
// Extension point for the state-serialization catch-all (plan P6).
//
// ObjectStateFormatter, session state and the output cache have native handling for the types that
// actually occur in view state - primitives, string, DateTime, Pair, Triplet, ArrayList, Hashtable,
// Unit, Color, arrays of those, and anything with a TypeConverter. Upstream fell back to
// BinaryFormatter for everything else; that is disabled on .NET 8 and removed in .NET 9.
//
// No substitute is faithful, because BinaryFormatter serialised arbitrary object graphs by walking
// private fields - including cycles and shared references. Silently swapping in JSON or
// DataContractSerializer would change what round-trips, and the failure would surface later as
// missing data rather than an error. So the default refuses, and an application that genuinely needs
// to put a custom type in view state or session opts in to a serializer it has chosen.
//
//     app.UseWebForms (o => o.StateSerializer = new JsonStateObjectSerializer ());
//
// Implementations must be thread-safe: requests serialize state concurrently.
//

using System;
using System.IO;

namespace System.Web
{
	/// <summary>
	/// Serializes objects the built-in view state / session formatters have no native encoding for.
	/// </summary>
	public interface IStateObjectSerializer
	{
		/// <summary>
		/// Whether this serializer handles <paramref name="type"/>. Returning false produces the same
		/// diagnostic as having no serializer installed, naming the type.
		/// </summary>
		bool CanSerialize (Type type);

		/// <summary>
		/// Writes <paramref name="graph"/> to <paramref name="stream"/>.
		/// The written form MUST be self-delimiting: these streams are shared with other state, and
		/// more data usually follows. Reading to the end of the stream on the way back is wrong.
		/// </summary>
		void Serialize (Stream stream, object graph);

		/// <summary>Reads back exactly what <see cref="Serialize"/> wrote, leaving the stream positioned after it.</summary>
		object Deserialize (Stream stream);
	}
}
