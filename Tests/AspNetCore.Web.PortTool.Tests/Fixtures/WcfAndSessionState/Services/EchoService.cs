using System.ServiceModel;

namespace LegacyWcf.Services
{
	[ServiceContract]
	public interface IEcho
	{
		[OperationContract]
		string Say (string text);
	}

	public class EchoService : IEcho
	{
		public string Say (string text) { return "echo: " + text; }
	}
}
