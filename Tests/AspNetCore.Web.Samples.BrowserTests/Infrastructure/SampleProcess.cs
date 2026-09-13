//
// Runs one sample application as a child process, the way `dotnet run` would.
//
// The executable is the sample's own build output; the content root is the sample's SOURCE directory,
// so web.config, the .aspx files and App_Code are the real ones - exactly as with `dotnet run`, whose
// working directory is the project directory. Kestrel is told to listen on port 0 and the actual address
// is read back from the "Now listening on:" line the host logs at startup, so parallel runs and a sample
// the developer already has open cannot collide on a port.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WebFormsPort.SamplesBrowserTests
{
	public sealed class SampleProcess : IAsyncDisposable
	{
		static readonly Regex listening = new Regex (@"Now listening on:\s*(?<url>http://\S+)", RegexOptions.Compiled);

		readonly Process process;
		readonly StringBuilder output = new StringBuilder ();
		readonly TaskCompletionSource<string> address =
			new TaskCompletionSource<string> (TaskCreationOptions.RunContinuationsAsynchronously);

		public string Name { get; }

		/// <summary>Base address, e.g. http://127.0.0.1:51234 (no trailing slash).</summary>
		public string BaseAddress { get; private set; }

		SampleProcess (string name, Process process)
		{
			Name = name;
			this.process = process;
		}

		/// <summary>Everything the sample wrote to stdout and stderr so far - attached to failures.</summary>
		public string Output {
			get { lock (output) return output.ToString (); }
		}

		public static async Task<SampleProcess> StartAsync (string name, string assemblyName = null)
		{
			string source = RepoPaths.Sample (name);
			string configuration = typeof (SampleProcess).Assembly.GetCustomAttributes<AssemblyMetadataAttribute> ()
				.Single (a => a.Key == "SampleConfiguration").Value;
			string dll = Path.Combine (source, "bin", configuration, "net10.0", (assemblyName ?? name) + ".dll");

			if (!File.Exists (dll))
				throw new FileNotFoundException (
					"The sample has not been built: " + dll + ". Build the solution (or this test project, " +
					"which references every sample) first.", dll);

			var info = new ProcessStartInfo (DotnetHost ()) {
				WorkingDirectory = source,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			info.ArgumentList.Add (dll);
			info.ArgumentList.Add ("--contentRoot");
			info.ArgumentList.Add (source);
			info.ArgumentList.Add ("--urls");
			info.ArgumentList.Add ("http://127.0.0.1:0");
			info.Environment ["ASPNETCORE_ENVIRONMENT"] = "Development";
			// The address is parsed from the console log; keep it in the plain single-line-per-field format.
			info.Environment ["Logging__Console__FormatterName"] = "simple";
			info.Environment ["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";

			var sample = new SampleProcess (name, new Process { StartInfo = info, EnableRaisingEvents = true });
			sample.process.OutputDataReceived += (s, e) => sample.OnLine (e.Data);
			sample.process.ErrorDataReceived += (s, e) => sample.OnLine (e.Data);
			sample.process.Exited += (s, e) => sample.address.TrySetException (new InvalidOperationException (
				name + " exited with code " + sample.process.ExitCode + " before it started listening.\n" + sample.Output));

			sample.process.Start ();
			sample.process.BeginOutputReadLine ();
			sample.process.BeginErrorReadLine ();

			using (var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (90)))
			using (timeout.Token.Register (() => sample.address.TrySetException (new TimeoutException (
				name + " did not report a listening address within 90s.\n" + sample.Output)))) {
				try {
					sample.BaseAddress = (await sample.address.Task).TrimEnd ('/');
				} catch {
					await sample.DisposeAsync ();
					throw;
				}
			}

			return sample;
		}

		void OnLine (string line)
		{
			if (line == null)
				return;

			lock (output)
				output.AppendLine (line);

			Match match = listening.Match (line);
			if (match.Success)
				address.TrySetResult (match.Groups ["url"].Value);
		}

		static string DotnetHost ()
		{
			// Set by the SDK for anything it launches, including the test host; falls back to PATH.
			string host = Environment.GetEnvironmentVariable ("DOTNET_HOST_PATH");
			return String.IsNullOrEmpty (host) ? "dotnet" : host;
		}

		public async ValueTask DisposeAsync ()
		{
			try {
				if (!process.HasExited) {
					process.Kill (entireProcessTree: true);
					await process.WaitForExitAsync ();
				}
			} catch (InvalidOperationException) {
				// Never started, or already gone.
			}

			process.Dispose ();
		}
	}
}
