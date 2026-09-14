//
// Tools/state-server as a separate process. Sessions stored through it live in THAT process, which is
// what lets a test prove the store is remote: restart it and they are gone.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WebFormsPort.RemotingTests
{
	public sealed class StateServerProcess : IDisposable
	{
		readonly Process process;

		StateServerProcess (Process process, int port)
		{
			this.process = process;
			Port = port;
		}

		public int Port { get; }

		public int ProcessId => process.Id;

		public static StateServerProcess Start (int port)
		{
			// Built alongside the tests (a build-ordering ProjectReference), in the same configuration.
			// bin/<configuration>/net10.0/ - BaseDirectory ends with a separator, which DirectoryInfo
			// would read as an empty last segment.
			string configuration = new DirectoryInfo (Path.TrimEndingDirectorySeparator (AppContext.BaseDirectory)).Parent.Name;
			string dll = Path.Combine (RepoPaths.Root, "Tools", "state-server", "bin", configuration, "net10.0", "state-server.dll");
			if (!File.Exists (dll))
				throw new FileNotFoundException ("Tools/state-server has not been built for " + configuration + ".", dll);

			var start = new ProcessStartInfo ("dotnet") {
				UseShellExecute = false,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			start.ArgumentList.Add ("exec");
			start.ArgumentList.Add (dll);
			start.ArgumentList.Add ("--port");
			start.ArgumentList.Add (port.ToString ());

			Process process = Process.Start (start);
			string line = process.StandardOutput.ReadLine ();
			if (line == null || !line.StartsWith ("LISTENING ", StringComparison.Ordinal)) {
				string error = process.StandardError.ReadToEnd ();
				process.Kill ();
				throw new InvalidOperationException (
					"The state server did not start on port " + port + " (is something else - such as the " +
					"Windows ASP.NET State Service - listening there?). Output: " + line + " " + error);
			}

			return new StateServerProcess (process, port);
		}

		public void Dispose ()
		{
			try {
				// Closing stdin is the tool's orderly stop; kill if it does not take.
				process.StandardInput.Close ();
				if (!process.WaitForExit (5000))
					process.Kill ();
			} catch (InvalidOperationException) {
			}
			process.Dispose ();
		}
	}
}
