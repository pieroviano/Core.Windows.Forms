//
// Stand-ins for the WSDL *import* types this port cannot provide.
//
// Importing a WSDL document to generate a client proxy needs XmlCodeExporter / SoapCodeExporter /
// SoapSchemaImporter and System.Xml.Serialization.Advanced. Verified against .NET 8: none of them
// exist - the XML serialization code-generation types were never ported to .NET Core, only the
// runtime ones (XmlReflectionImporter, SoapReflectionImporter, XmlSchemaExporter/Importer,
// XmlSerializer). So ServiceDescriptionImporter, WebReference and the protocol importers are
// excluded from this build.
//
// They cannot simply vanish, because two retained pieces reference them:
//
//   * WebServicesInteroperability.CheckConformance (WsiProfiles, WebReference, ...) - and that file
//     also declares BasicProfileViolation / BasicProfileViolationCollection, which ProtocolReflector
//     needs for the WS-I check during WSDL generation and which the .asmx help page renders;
//   * DefaultWsdlHelpGenerator.aspx's GetProxyCode (), the "client proxy" tab of the .asmx help page.
//
// So the types exist and throw when used, with a message that says what is missing and what to do
// instead. SERVING .asmx, and generating WSDL for a service, both work - only consuming WSDL does not.
//

using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Specialized;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace System.Web.Services.Description
{
	static class WsdlImportUnavailable
	{
		internal const string Message =
			"Importing WSDL to generate a client proxy is not supported by this port of " +
			"System.Web.Services: it requires the XML serialization code-generation types " +
			"(XmlCodeExporter, SoapCodeExporter, SoapSchemaImporter, System.Xml.Serialization.Advanced), " +
			"which .NET Core does not provide. Serving .asmx and generating WSDL for a service both " +
			"work. To consume a service, use a proxy generated ahead of time, or dotnet-svcutil.";
	}

	/// <summary>
	/// Present so that code referencing it still compiles; every member throws. See
	/// <see cref="WsdlImportUnavailable"/> for why.
	/// </summary>
	public sealed class WebReference
	{
		public IDictionary Documents {
			get { throw new PlatformNotSupportedException (WsdlImportUnavailable.Message); }
		}
	}

	/// <summary>
	/// Present so that code referencing it still compiles; every member throws. See
	/// <see cref="WsdlImportUnavailable"/> for why.
	/// </summary>
	public sealed class ServiceDescriptionImporter
	{
		public ServiceDescriptionImporter ()
		{
		}

		public XmlSchemas Schemas {
			get { throw new PlatformNotSupportedException (WsdlImportUnavailable.Message); }
		}

		public void AddServiceDescription (ServiceDescription serviceDescription, string appSettingUrlKey,
						   string appSettingBaseUrl)
		{
			throw new PlatformNotSupportedException (WsdlImportUnavailable.Message);
		}

		public void Import (CodeNamespace codeNamespace, CodeCompileUnit codeCompileUnit)
		{
			throw new PlatformNotSupportedException (WsdlImportUnavailable.Message);
		}

		// Called by the retained WebServicesInteroperability.CheckConformance overload.
		internal static void AddDocument (string path, object document, XmlSchemas schemas,
						  ServiceDescriptionCollection descriptions, StringCollection warnings)
		{
			throw new PlatformNotSupportedException (WsdlImportUnavailable.Message);
		}
	}
}
