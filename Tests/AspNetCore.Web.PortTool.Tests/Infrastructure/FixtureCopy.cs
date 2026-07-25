//
// Locating the fixtures, and copying one somewhere safe to convert it.
//
// The tool RENAMES and REWRITES files, so nothing may ever run against Fixtures/ in place - the first
// test would consume the fixture and every test after it would be converting a converted project. Each
// test gets its own copy in a temp directory and deletes it afterwards.
//

using System;
using System.IO;

namespace WebFormsPort.PortToolTests
{
	sealed class FixtureCopy : IDisposable
	{
		FixtureCopy (string directory)
		{
			Directory = directory;
		}

		/// <summary>Absolute path of the working copy.</summary>
		public string Directory { get; }

		public string Path (string relative)
		{
			return System.IO.Path.Combine (Directory, relative.Replace ('/', System.IO.Path.DirectorySeparatorChar));
		}

		public bool Exists (string relative)
		{
			return File.Exists (Path (relative));
		}

		public string Read (string relative)
		{
			return File.ReadAllText (Path (relative));
		}

		/// <summary>Every file in the copy, as relative paths with forward slashes, sorted.</summary>
		public string [] Snapshot ()
		{
			var files = System.IO.Directory.GetFiles (Directory, "*", SearchOption.AllDirectories);
			Array.Sort (files, StringComparer.Ordinal);

			var relative = new string [files.Length];
			for (int i = 0; i < files.Length; i++)
				relative [i] = System.IO.Path.GetRelativePath (Directory, files [i]).Replace ('\\', '/') +
					       ":" + new FileInfo (files [i]).Length;

			return relative;
		}

		public static FixtureCopy Of (string fixtureName)
		{
			string source = System.IO.Path.Combine (FixtureRoot, fixtureName);
			if (!System.IO.Directory.Exists (source))
				throw new DirectoryNotFoundException ("No fixture named '" + fixtureName + "' under " + FixtureRoot);

			string destination = System.IO.Path.Combine (
				System.IO.Path.GetTempPath (), "port-tool-tests",
				fixtureName + "-" + Guid.NewGuid ().ToString ("n").Substring (0, 8));

			CopyDirectory (source, destination);
			return new FixtureCopy (destination);
		}

		/// <summary>
		/// Fixtures live in the SOURCE tree, not the test output. They are excluded from every item
		/// glob (see the csproj), so there is nothing to copy to bin\ - and reading them from source
		/// keeps the one copy of the truth in one place.
		/// </summary>
		public static string FixtureRoot {
			get {
				var directory = new DirectoryInfo (AppContext.BaseDirectory);

				while (directory != null) {
					string candidate = System.IO.Path.Combine (directory.FullName, "Fixtures");
					if (System.IO.Directory.Exists (candidate) &&
					    System.IO.Directory.Exists (System.IO.Path.Combine (candidate, "WebFormsCSharp")))
						return candidate;

					directory = directory.Parent;
				}

				throw new InvalidOperationException (
					"Could not find the Fixtures directory walking up from " + AppContext.BaseDirectory);
			}
		}

		/// <summary>Repository root, for the local package feed the build tests need.</summary>
		public static string RepositoryRoot {
			get {
				var directory = new DirectoryInfo (AppContext.BaseDirectory);

				while (directory != null) {
					if (File.Exists (System.IO.Path.Combine (directory.FullName, "AspNetCore.Web.slnx")))
						return directory.FullName;

					directory = directory.Parent;
				}

				throw new InvalidOperationException (
					"Could not locate AspNetCore.Web.slnx walking up from " + AppContext.BaseDirectory);
			}
		}

		static void CopyDirectory (string source, string destination)
		{
			System.IO.Directory.CreateDirectory (destination);

			foreach (string file in System.IO.Directory.GetFiles (source, "*", SearchOption.AllDirectories)) {
				string relative = System.IO.Path.GetRelativePath (source, file);
				string target = System.IO.Path.Combine (destination, relative);

				System.IO.Directory.CreateDirectory (System.IO.Path.GetDirectoryName (target));
				File.Copy (file, target, overwrite: true);
			}
		}

		/// <summary>
		/// The private NuGet cache belonging to this fixture: a SIBLING directory, not one inside it.
		/// </summary>
		/// <remarks>
		/// It cannot live under <see cref="Directory"/>, because that is the project directory and the
		/// SDK globs **/*.cs from it - every .cs a package ships in contentFiles would join the
		/// compilation. It is declared here rather than composed at the call site so that Dispose knows
		/// about it: a full package cache is hundreds of megabytes, and the build suite creates one per
		/// test.
		/// </remarks>
		public string PackageCache {
			get { return Directory + "-packages"; }
		}

		public void Dispose ()
		{
			Delete (Directory);
			Delete (PackageCache);
		}

		static void Delete (string directory)
		{
			try {
				if (System.IO.Directory.Exists (directory))
					System.IO.Directory.Delete (directory, recursive: true);
			} catch (IOException) {
				// A temp directory that outlives one test run is untidy, not a failure - and on
				// Windows a just-finished dotnet build can still hold a handle for a moment.
			} catch (UnauthorizedAccessException) {
				// Same, for a file left read-only by an extraction.
			}
		}
	}
}
