//
// Stands in for System.ServiceModel / System.ServiceModel.Description, referenced by exactly one
// upstream file: System.Web.Script.Services/LogicalTypeInfo.cs.
//
// That file has two halves. AsmxLogicalTypeInfo drives page methods and [ScriptService] .asmx
// services - the JSON/AJAX bridge that this port DOES ship, now that System.Web.Services is ported
// as Core.Web.Services. WcfLogicalTypeInfo drives script access to a WCF [ServiceContract] type
// hosted at a .svc endpoint, which nothing in this port hosts and which would need the whole
// System.ServiceModel stack to describe.
//
// Rather than fork the 676-line file into Overrides/ to delete its second half - a large, permanent
// divergence to keep re-merging - the file stays compiled in place and its two `using` lines are
// retargeted here by tools/port-patches.txt. The WCF half then compiles against these types and is
// simply never reached: the dispatch in LogicalTypeInfo.CreateTypeInfo is likewise patched to call
// WcfNotSupported.IsServiceContract, which always answers false.
//
// These names deliberately live in System.Web.Script.Services.Port, NOT in System.ServiceModel.
// Declaring types into the real WCF namespaces would collide (CS0433) with
// System.ServiceModel.Primitives the moment an application referenced it to call a WCF service -
// a perfectly reasonable thing for a migrated app to do.
//

using System;
using System.Collections.Generic;
using System.Reflection;

namespace System.Web.Script.Services.Port
{
	static class WcfNotSupported
	{
		const string RealServiceContract = "System.ServiceModel.ServiceContractAttribute";

		internal const string Message =
			"This type is a WCF service contract ([ServiceContract]). WCF services - .svc endpoints and " +
			"their client script proxies - are not hosted by this port; only ASP.NET page methods and " +
			"[ScriptService] .asmx web services are. Expose the operation as a [WebMethod] on an .asmx " +
			"service or as an ASP.NET Core endpoint.";

		// Replaces the upstream `t.GetCustomAttributes (typeof (ServiceContractAttribute), false).Length > 0`
		// test. Matching by full name rather than by type identity is what lets this work at all: the
		// application's [ServiceContract] comes from the real System.ServiceModel.Primitives, which this
		// assembly does not reference.
		//
		// Always returns false - it either throws or the caller takes the ASMX branch. Failing loudly
		// here is better than upstream's silent fall-through would be: an unrecognised WCF contract
		// would otherwise be described as if it were an .asmx service and emit a proxy whose every call
		// 404s at runtime.
		internal static bool IsServiceContract (Type t)
		{
			foreach (var attribute in t.GetCustomAttributes (false))
				if (attribute.GetType ().FullName == RealServiceContract)
					throw new PlatformNotSupportedException (Message);

			return false;
		}
	}

	// The shape of System.ServiceModel.Description that LogicalTypeInfo.cs touches, and nothing more.
	// Every member is unreachable; GetContract is the single entry point and it throws.
	sealed class ContractDescription
	{
		ContractDescription () { }

		internal static ContractDescription GetContract (Type type)
		{
			throw new PlatformNotSupportedException (WcfNotSupported.Message);
		}

		internal IEnumerable<OperationDescription> Operations {
			get { throw new PlatformNotSupportedException (WcfNotSupported.Message); }
		}
	}

	sealed class OperationDescription
	{
		OperationDescription () { }

		internal string Name {
			get { throw new PlatformNotSupportedException (WcfNotSupported.Message); }
		}

		internal MethodInfo SyncMethod {
			get { throw new PlatformNotSupportedException (WcfNotSupported.Message); }
		}

		internal IEnumerable<MessageDescription> Messages {
			get { throw new PlatformNotSupportedException (WcfNotSupported.Message); }
		}
	}

	sealed class MessageDescription
	{
		MessageDescription () { }

		internal MessageBodyDescription Body {
			get { throw new PlatformNotSupportedException (WcfNotSupported.Message); }
		}
	}

	sealed class MessageBodyDescription
	{
		MessageBodyDescription () { }

		internal IEnumerable<MessagePartDescription> Parts {
			get { throw new PlatformNotSupportedException (WcfNotSupported.Message); }
		}

		internal MessagePartDescription ReturnValue {
			get { throw new PlatformNotSupportedException (WcfNotSupported.Message); }
		}
	}

	sealed class MessagePartDescription
	{
		MessagePartDescription () { }

		internal Type Type {
			get { throw new PlatformNotSupportedException (WcfNotSupported.Message); }
		}
	}
}
