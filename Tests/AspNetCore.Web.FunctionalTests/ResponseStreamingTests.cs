//
// The response path: streaming, large file sends, and what happens when the client goes away.
//
// Every other suite here asserts on a COMPLETE response body, and that is precisely what cannot
// distinguish a streaming runtime from one that buffers everything and writes it at the end - the
// bytes are identical either way. These tests read the response as it arrives instead.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WebFormsPort.FunctionalTests
{
	[Collection (WebFormsCollection.Name)]
	public class ResponseStreamingTests
	{
		readonly SampleAppFixture fixture;

		public ResponseStreamingTests (SampleAppFixture fixture)
		{
			this.fixture = fixture;
		}

		[Fact]
		public async Task A_flushed_chunk_reaches_the_client_before_the_page_ends ()
		{
			// Streaming.aspx writes three chunks with a 250ms pause between them. If Response.Flush ()
			// works, the first chunk is readable long before the last is written; if the runtime buffers
			// to the end, nothing is readable until roughly 750ms have passed.
			using HttpClient client = fixture.CreateClient ();

			// Warm first. The initial request for any page pays for its compilation - seconds, not
			// milliseconds - which would swamp the measurement and make a streaming runtime look like a
			// buffering one.
			await client.GetStringAsync ("/Streaming.aspx");

			var stopwatch = Stopwatch.StartNew ();

			using HttpResponseMessage response = await client.GetAsync (
				"/Streaming.aspx", HttpCompletionOption.ResponseHeadersRead);

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);

			using Stream stream = await response.Content.ReadAsStreamAsync ();
			var reader = new StreamReader (stream);

			string first = await ReadChunkAsync (reader);
			TimeSpan untilFirstChunk = stopwatch.Elapsed;

			Assert.Equal ("chunk0", first);

			// Generous, because CI machines are slow and the point is the ORDER of magnitude: the whole
			// page takes ~750ms, so arriving in under 500ms means the first chunk did not wait for the
			// last. A buffering runtime cannot pass this no matter how fast the machine is.
			Assert.True (untilFirstChunk < TimeSpan.FromMilliseconds (500),
				     $"the first chunk took {untilFirstChunk.TotalMilliseconds:F0}ms, which means the " +
				     "response was buffered until the page finished rather than flushed");

			Assert.Equal ("chunk1", await ReadChunkAsync (reader));
			Assert.Equal ("chunk2", await ReadChunkAsync (reader));
		}

		// Blank lines are the .aspx template's own literal whitespace - the newlines around the
		// directive and the code block - and carry no meaning here. Skipping them keeps the test about
		// WHEN each chunk arrives rather than about how the page happens to be laid out.
		static async Task<string> ReadChunkAsync (StreamReader reader)
		{
			string line;
			do {
				line = await reader.ReadLineAsync ();
			} while (line != null && line.Length == 0);

			return line;
		}

		[Fact]
		public async Task A_transmitted_file_arrives_whole_and_correct ()
		{
			// TransmitFile streams to the client rather than accumulating the file in memory. The
			// assertion here is correctness - every byte, in order - because a streaming implementation
			// is the easy place to drop or duplicate a chunk. The memory behaviour is asserted below.
			using HttpClient client = fixture.CreateClient ();

			byte [] payload = await client.GetByteArrayAsync ("/Download.ashx");

			Assert.Equal (3 * 1024 * 1024, payload.Length);

			for (int i = 0; i < payload.Length; i += 7919)          // a prime stride: cheap, and it will
				Assert.Equal ((byte) (i % 251), payload [i]);   // catch an offset error anywhere
		}

		[Fact]
		public async Task A_large_file_is_streamed_rather_than_held_in_memory ()
		{
			// The response body is DISCARDED as it arrives, in 64 KB chunks, and that is the whole design
			// of this test. An earlier version read the file into a byte[] and measured total allocation,
			// which cannot work: the fixture hosts the server in this same process, so the client's own
			// buffers - and HttpClient doubles them as it grows - are counted too, and they dwarf the
			// difference being looked for. Discarding on the client makes any large allocation the
			// server's by construction.
			//
			// Buffering a 32 MB send would allocate at least 32 MB, on the large object heap. Streaming
			// allocates one 64 KB chunk.
			using HttpClient client = fixture.CreateClient ();

			using HttpResponseMessage response = await client.GetAsync (
				"/Download.ashx?size=" + (32 * 1024 * 1024), HttpCompletionOption.ResponseHeadersRead);

			Assert.Equal (HttpStatusCode.OK, response.StatusCode);

			long before = GC.GetTotalAllocatedBytes (precise: true);
			long total = 0;

			using (Stream stream = await response.Content.ReadAsStreamAsync ()) {
				var sink = new byte [64 * 1024];
				int read;
				while ((read = await stream.ReadAsync (sink, 0, sink.Length)) > 0)
					total += read;
			}

			long allocated = GC.GetTotalAllocatedBytes (precise: true) - before;

			Assert.Equal (32L * 1024 * 1024, total);
			Assert.True (allocated < 16L * 1024 * 1024,
				     $"{allocated / (1024 * 1024)} MB allocated while streaming a 32 MB file to a " +
				     "discarding client, which means the server accumulated it rather than streaming it");
		}

		[Fact]
		public async Task A_client_that_disconnects_mid_response_does_not_fault_the_server ()
		{
			// The back button, a closed tab, a reload. The server must treat it as the ordinary end of an
			// abandoned request rather than a fault - and, critically, must still be healthy afterwards.
			//
			// The disconnect is done by ABANDONING the response mid-body, not by cancelling the GetAsync
			// task: now that the response streams, GetAsync returns as soon as the first chunk arrives, so
			// there is nothing left pending to cancel. Disposing the response while the server is still
			// writing is what actually drops the connection under it.
			using (HttpClient client = fixture.CreateClient ()) {
				HttpResponseMessage response = await client.GetAsync (
					"/Streaming.aspx", HttpCompletionOption.ResponseHeadersRead);

				Stream stream = await response.Content.ReadAsStreamAsync ();

				var head = new byte [6];
				int read = await stream.ReadAsync (head, 0, head.Length);
				Assert.True (read > 0, "no bytes arrived before the disconnect, so nothing was abandoned");

				// Still mid-render: the page has two more chunks and 500ms of sleeps to go.
				stream.Dispose ();
				response.Dispose ();
			}

			// Give the server a moment to notice and run its finally.
			await Task.Delay (500);

			// The assertion that matters. A middleware that let the abort escape would have faulted the
			// request; this proves it did not take anything with it.
			using HttpClient after = fixture.CreateClient ();
			string html = await after.GetStringAsync ("/Default.aspx");

			Assert.Contains ("<html", html, StringComparison.OrdinalIgnoreCase);

			// And the streaming page itself still works, which a corrupted per-request state would break.
			string again = await after.GetStringAsync ("/Streaming.aspx");
			Assert.Contains ("chunk2", again);
		}
	}
}
