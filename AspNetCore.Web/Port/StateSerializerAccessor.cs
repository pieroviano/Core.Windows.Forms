//
// Lets the hosting assembly set the state serializer without exposing StateSerializer's internals.
//
// StateSerializer is internal port plumbing (System.Web.Util), while IStateObjectSerializer is public
// API that applications implement. This is the seam between them.
//

namespace System.Web.Util
{
	static class StateSerializerAccessor
	{
		internal static IStateObjectSerializer Get ()
		{
			return StateSerializer.Serializer;
		}

		internal static void Set (IStateObjectSerializer serializer)
		{
			StateSerializer.Serializer = serializer;
		}
	}
}
