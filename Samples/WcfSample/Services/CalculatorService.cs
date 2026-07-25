using CoreWCF;

namespace WcfSample.Services
{
	// [ServiceContract] on the implementing class rather than on an interface. WCF has always allowed
	// this, and plenty of ported .svc files do it, so the bridge has to find the contract either way.
	//
	// It also sits in a sub-directory (/Api/Calculator.svc), which is what fixes its address: the
	// endpoint is reachable at the path the file itself has, not at a route configured anywhere.
	[ServiceContract (Namespace = "http://webformsport.example/")]
	public class CalculatorService
	{
		[OperationContract]
		public int Multiply (int a, int b)
		{
			return a * b;
		}
	}
}
