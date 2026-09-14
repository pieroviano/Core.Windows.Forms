//
// state-server [--port <n>] [--allow-remote]
//
// Holds session state for applications configured <sessionState mode="StateServer"> that call
// UseWebFormsRemoteStateServer. Defaults are aspnet_state.exe's: port 42424, loopback connections only.
// Prints "LISTENING <port>" once ready, so a script or test can wait for it; runs until Ctrl+C or until
// its standard input closes.
//

using System;
using System.Globalization;
using System.Threading;
using System.Web.SessionState;

namespace StateServer
{
	static class Program
	{
		static int Main (string [] args)
		{
			int port = RemoteStateServerHost.DefaultPort;
			bool allowRemote = false;

			for (int i = 0; i < args.Length; i++) {
				switch (args [i]) {
				case "--port" when i + 1 < args.Length:
					if (!Int32.TryParse (args [++i], NumberStyles.None, CultureInfo.InvariantCulture, out port)) {
						Console.Error.WriteLine ("--port needs a number, got '" + args [i] + "'.");
						return 2;
					}
					break;
				case "--allow-remote":
					allowRemote = true;
					break;
				default:
					Console.Error.WriteLine ("usage: state-server [--port <n>] [--allow-remote]");
					return 2;
				}
			}

			using (RemoteStateServerHost host = RemoteStateServerHost.Start (port, allowRemote)) {
				Console.WriteLine ("LISTENING " + host.Port.ToString (CultureInfo.InvariantCulture));

				var stop = new ManualResetEventSlim ();
				Console.CancelKeyPress += (sender, e) => {
					e.Cancel = true;
					stop.Set ();
				};

				// A parent that launched this with redirected input stops it by closing that stream, which
				// also covers the parent dying without cleaning up.
				if (Console.IsInputRedirected) {
					new Thread (() => {
						try {
							while (Console.In.ReadLine () != null) {
							}
						} catch (Exception) {
						}
						stop.Set ();
					}) { IsBackground = true }.Start ();
				}

				stop.Wait ();
			}

			return 0;
		}
	}
}
