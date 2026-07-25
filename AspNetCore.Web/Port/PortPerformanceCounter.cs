//
// Stand-in for the two Windows performance counters the upstream tree publishes.
//
// Upstream creates PerformanceCounter ("ASP.NET", "Requests Queued") and ("ASP.NET",
// "Requests Total") and writes to them. That category is registered by the .NET Framework ASP.NET
// installer, which is not present here, so the counters come back read-only and the first write
// throws InvalidOperationException ("Cannot update Performance Counter, this object has been
// initialized as ReadOnly") from HttpRuntime's static constructor - i.e. before anything can serve
// a request.
//
// PerformanceCounter is sealed, so tools/port-patches.txt retargets the two field declarations and
// their initialisers at this type instead. Counts are kept in memory: cheap, thread-safe, and they
// preserve the shape of the upstream code so these can later be projected onto
// System.Diagnostics.Metrics without touching the ported sources again.
//

using System.Threading;

namespace System.Web.Util
{
	sealed class PortPerformanceCounter
	{
		readonly string category;
		readonly string counter;
		long raw;

		public PortPerformanceCounter (string categoryName, string counterName)
		{
			category = categoryName;
			counter = counterName;
		}

		public string CategoryName {
			get { return category; }
		}

		public string CounterName {
			get { return counter; }
		}

		public long RawValue {
			get { return Interlocked.Read (ref raw); }
			set { Interlocked.Exchange (ref raw, value); }
		}

		public long Increment ()
		{
			return Interlocked.Increment (ref raw);
		}

		public long Decrement ()
		{
			return Interlocked.Decrement (ref raw);
		}

		public long IncrementBy (long value)
		{
			return Interlocked.Add (ref raw, value);
		}

		public void Close ()
		{
		}

		public void Dispose ()
		{
		}
	}
}
