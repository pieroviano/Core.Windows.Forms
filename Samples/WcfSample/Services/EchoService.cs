using CoreWCF;

namespace WcfSample.Services
{
	// An ordinary WCF service contract. The attributes come from CoreWCF rather than
	// System.ServiceModel, but the shape - and the SOAP on the wire - is the same.
	[ServiceContract (Namespace = "http://webformsport.example/")]
	public interface IEcho
	{
		[OperationContract]
		string Say (string text);

		[OperationContract]
		int Add (int a, int b);
	}

	public class EchoService : IEcho
	{
		public string Say (string text)
		{
			return "wcf echo: " + text;
		}

		public int Add (int a, int b)
		{
			return a + b;
		}
	}
}
