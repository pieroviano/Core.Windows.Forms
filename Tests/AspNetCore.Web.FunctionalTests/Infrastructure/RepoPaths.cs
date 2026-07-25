//
// Locates the repository on disk from the test assembly's output directory.
//
// The tests drive Samples/WebFormsSample from its SOURCE directory rather than a copy in the test
// output: that directory is the application - web.config, the .aspx/.ascx/.master files, App_Code,
// App_GlobalResources, App_Themes and the Secure/ sub-application with its own web.config. Copying
// it would mean keeping the copy in step, and MapPath/PhysicalApplicationPath assertions would then
// be describing the copy rather than the real layout.
//

using System;
using System.IO;

namespace WebFormsPort
{
	static class RepoPaths
	{
		static readonly Lazy<string> root = new Lazy<string> (FindRoot);

		/// <summary>Repository root - the directory holding AspNetCore.Web.slnx.</summary>
		public static string Root {
			get { return root.Value; }
		}

		/// <summary>Physical path of the C# WebForms sample application.</summary>
		public static string SampleApp {
			get { return Sample ("WebFormsSample"); }
		}

		/// <summary>Physical path of the VB.NET WebForms sample application.</summary>
		public static string SampleAppVB {
			get { return Sample ("WebFormsSampleVB"); }
		}

		/// <summary>Physical path of a sample application by directory name.</summary>
		public static string Sample (string name)
		{
			return Path.Combine (Root, "Samples", name);
		}

		static string FindRoot ()
		{
			var dir = new DirectoryInfo (AppContext.BaseDirectory);

			while (dir != null) {
				if (File.Exists (Path.Combine (dir.FullName, "AspNetCore.Web.slnx")))
					return dir.FullName;
				dir = dir.Parent;
			}

			throw new InvalidOperationException (
				"Could not locate AspNetCore.Web.slnx walking up from " + AppContext.BaseDirectory +
				". The functional tests read the sample application from its source directory and " +
				"cannot run outside the repository.");
		}
	}
}
