//
// System.Runtime.Remoting.Messaging.CallContext shim
//
// The upstream tree stores HttpContext.Current in a logical call context slot
// (System.Web/HttpContext.cs). Remoting is gone on .NET Core, so we reimplement the two
// members that are actually used on top of AsyncLocal.
//
// AsyncLocal is the correct semantic here, not [ThreadStatic]: the value must flow across
// await boundaries within one request but must not leak to sibling requests. The slot table
// is therefore copy-on-write - mutating a dictionary shared by reference would let one
// request's SetData be observed by another that branched from the same execution context.
//

using System.Collections.Generic;
using System.Threading;

namespace System.Runtime.Remoting.Messaging
{
	internal static class CallContext
	{
		static readonly AsyncLocal<Dictionary<string, object>> slots =
			new AsyncLocal<Dictionary<string, object>> ();

		public static object GetData (string name)
		{
			Dictionary<string, object> table = slots.Value;
			object value;
			if (table != null && table.TryGetValue (name, out value))
				return value;
			return null;
		}

		public static void SetData (string name, object data)
		{
			Dictionary<string, object> current = slots.Value;
			Dictionary<string, object> copy = current == null
				? new Dictionary<string, object> (StringComparer.Ordinal)
				: new Dictionary<string, object> (current, StringComparer.Ordinal);
			copy [name] = data;
			slots.Value = copy;
		}

		public static void FreeNamedDataSlot (string name)
		{
			Dictionary<string, object> current = slots.Value;
			if (current == null || !current.ContainsKey (name))
				return;
			Dictionary<string, object> copy = new Dictionary<string, object> (current, StringComparer.Ordinal);
			copy.Remove (name);
			slots.Value = copy;
		}

		public static object HostContext { get; set; }

		public static object LogicalGetData (string name)
		{
			return GetData (name);
		}

		public static void LogicalSetData (string name, object data)
		{
			SetData (name, data);
		}
	}
}
