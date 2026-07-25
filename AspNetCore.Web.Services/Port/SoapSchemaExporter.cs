//
// Stand-in for System.Xml.Serialization.SoapSchemaExporter.
//
// .NET Core carries the *runtime* half of XML serialization - XmlReflectionImporter,
// SoapReflectionImporter, XmlSchemaExporter, XmlSchemaImporter, XmlSerializer - but not the code
// generation half. Verified against .NET 8: XmlSchemaExporter and XmlSchemaImporter are present,
// while XmlCodeExporter, SoapCodeExporter, SoapSchemaImporter, SoapSchemaExporter and the whole
// System.Xml.Serialization.Advanced namespace are missing.
//
// SoapSchemaExporter is the odd one out: it is not client-proxy codegen, it is used *server side* by
// SoapProtocolReflector when generating WSDL - but only for the SOAP-ENCODED (rpc/encoded) parts of a
// service description. Document/literal services, which is what almost every .asmx written this
// century uses, go through XmlSchemaExporter instead and never reach this type.
//
// Without this shim the whole System.Web.Services.Description subsystem fails to compile, taking
// ?wsdl generation with it for document/literal services too. With it, document/literal services get
// working WSDL and only rpc/encoded ones fail - with a message that says so.
//
// Exactly two members are exercised: the constructor and ExportMembersMapping
// (SoapProtocolReflector.cs lines 52, 91, 93, 154).
//

using System;
using System.Xml.Serialization;

namespace System.Xml.Serialization
{
	internal sealed class SoapSchemaExporter
	{
		const string Message =
			"Generating WSDL for a SOAP-encoded (rpc/encoded) web service requires " +
			"System.Xml.Serialization.SoapSchemaExporter, which .NET Core does not provide - the XML " +
			"serialization code-generation types were not ported. Document/literal services, the " +
			"default for .asmx, generate WSDL normally. Declare the service with " +
			"[SoapDocumentService] / [SoapDocumentMethod] to use document/literal.";

		readonly XmlSchemas schemas;

		internal SoapSchemaExporter (XmlSchemas schemas)
		{
			// Constructing is harmless and happens lazily for every service; only exporting an
			// encoded mapping is unsupported, so the failure is deferred to that point.
			this.schemas = schemas;
		}

		internal XmlSchemas Schemas {
			get { return schemas; }
		}

		internal void ExportMembersMapping (XmlMembersMapping xmlMembersMapping)
		{
			throw new PlatformNotSupportedException (Message);
		}

		internal void ExportMembersMapping (XmlMembersMapping xmlMembersMapping, bool exportEnclosingType)
		{
			throw new PlatformNotSupportedException (Message);
		}

		internal void ExportTypeMapping (XmlTypeMapping xmlTypeMapping)
		{
			throw new PlatformNotSupportedException (Message);
		}
	}
}
