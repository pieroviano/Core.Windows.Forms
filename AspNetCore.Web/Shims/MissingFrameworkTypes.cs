//
// Shims for framework types the upstream tree references that do not exist on .NET Core.
//
// Everything here is either (a) inert metadata that only a design-time host would ever load,
// or (b) a feature explicitly out of scope for the port. Nothing here silently changes
// behaviour: the out-of-scope members throw PlatformNotSupportedException rather than
// returning a plausible-looking wrong answer.
//

using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Runtime.Serialization;

namespace System.Drawing.Design
{
	// Referenced only by [Editor (..., typeof (UITypeEditor))] attributes on control properties.
	// Attribute arguments are metadata; a designer would supply the real type. At runtime this
	// is never instantiated.
	public class UITypeEditor
	{
		public UITypeEditor ()
		{
		}

		public virtual object EditValue (ITypeDescriptorContext context, IServiceProvider provider, object value)
		{
			return value;
		}

		public virtual bool GetPaintValueSupported ()
		{
			return false;
		}

		public virtual bool IsDropDownResizable {
			get { return false; }
		}
	}
}

namespace System.EnterpriseServices
{
	// Used by System.Web.Util.Transactions and the <%@ Page Transaction="..." %> directive.
	// COM+ transactions are out of scope; the enum only needs to exist so the directive parses.
	public enum TransactionOption
	{
		Disabled = 0,
		NotSupported = 1,
		Supported = 2,
		Required = 3,
		RequiresNew = 4
	}
}

namespace System.Net.Configuration
{
	// MailDefinition reads system.net/mailSettings/smtp to default the From address.
	// Only From is consumed by the upstream code.
	public sealed class SmtpSection : ConfigurationSection
	{
		static readonly ConfigurationProperty fromProp =
			new ConfigurationProperty ("from", typeof (string), null);
		static readonly ConfigurationProperty deliveryMethodProp =
			new ConfigurationProperty ("deliveryMethod", typeof (string), "network");
		static readonly ConfigurationPropertyCollection props;

		static SmtpSection ()
		{
			props = new ConfigurationPropertyCollection ();
			props.Add (fromProp);
			props.Add (deliveryMethodProp);
		}

		[ConfigurationProperty ("from")]
		public string From {
			get { return (string) base [fromProp]; }
			set { base [fromProp] = value; }
		}

		[ConfigurationProperty ("deliveryMethod", DefaultValue = "network")]
		public string DeliveryMethod {
			get { return (string) base [deliveryMethodProp]; }
			set { base [deliveryMethodProp] = value; }
		}

		// `protected internal` matches Mono's ConfigurationElement.Properties. That is legal
		// across assemblies here because Core.Configuration declares Core.Web a friend - which is
		// also why the whole ported tree can keep upstream's accessibility unchanged.
		protected internal override ConfigurationPropertyCollection Properties {
			get { return props; }
		}
	}
}

namespace System.Runtime.Serialization.Formatters.Soap
{
	// Referenced by the ResX reader for the (extremely rare) SOAP-serialized resource entries.
	// SOAP serialization does not exist on .NET Core and is not being reimplemented.
	public sealed class SoapFormatter : IFormatter
	{
		public SerializationBinder Binder { get; set; }
		public StreamingContext Context { get; set; }
		public ISurrogateSelector SurrogateSelector { get; set; }

		public object Deserialize (Stream serializationStream)
		{
			throw new PlatformNotSupportedException (
				"SOAP-serialized resources are not supported by this port of System.Web. " +
				"Re-save the .resx entry using binary or string serialization.");
		}

		public void Serialize (Stream serializationStream, object graph)
		{
			throw new PlatformNotSupportedException (
				"SOAP-serialized resources are not supported by this port of System.Web.");
		}
	}
}
