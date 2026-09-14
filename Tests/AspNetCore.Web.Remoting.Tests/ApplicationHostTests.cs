//
// ApplicationHost.CreateApplicationHost and ApplicationManager: each application in a child domain,
// which here is a separate process with HttpRuntime initialised for it.
//

using System;
using System.Linq;
using System.Web.Hosting;
using RemotingSample;
using Xunit;

namespace WebFormsPort.RemotingTests
{
	[Collection (RemotingCollection.Name)]
	public class ApplicationHostTests
	{
		readonly RemotingSampleFixture fixture;

		public ApplicationHostTests (RemotingSampleFixture fixture)
		{
			this.fixture = fixture;
		}

		static string SamplePath => RepoPaths.Sample ("RemotingSample");

		[Fact]
		public void CreateApplicationHost_creates_the_host_in_its_own_initialised_domain ()
		{
			var host = (ApplicationProbe) ApplicationHost.CreateApplicationHost (typeof (ApplicationProbe), "/probe", SamplePath);

			Assert.NotEqual (Environment.ProcessId, host.ProcessId);
			Assert.Equal ("/probe/", host.ApplicationVirtualPath.TrimEnd ('/') + "/");
			Assert.StartsWith (SamplePath.TrimEnd ('\\', '/'), host.ApplicationPhysicalPath.TrimEnd ('\\', '/'),
					   StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public void A_host_renders_a_page_through_the_full_pipeline_in_its_domain ()
		{
			var host = (ApplicationProbe) ApplicationHost.CreateApplicationHost (typeof (ApplicationProbe), "/", SamplePath);

			string html = host.Render ("Default.aspx", "");

			Assert.Contains ("pid: " + host.ProcessId, html);
		}

		[Fact]
		public void A_host_type_that_is_not_marshal_by_ref_is_refused ()
		{
			Assert.Throws<ArgumentException> (() => ApplicationHost.CreateApplicationHost (typeof (DivisionResult), "/", SamplePath));
		}

		[Fact]
		public void ApplicationManager_keeps_one_domain_and_one_object_per_type_per_application ()
		{
			ApplicationManager manager = ApplicationManager.GetApplicationManager ();
			const string appId = "remoting-tests-app";
			try {
				var first = (RegisteredProbe) manager.CreateObject (appId, typeof (RegisteredProbe), "/managed", SamplePath, false);
				var again = (RegisteredProbe) manager.CreateObject (appId, typeof (RegisteredProbe), "/managed", SamplePath, false);

				Assert.NotEqual (Environment.ProcessId, first.ProcessId);
				Assert.Equal (first.ProcessId, again.ProcessId);
				Assert.Same (first, again);
				Assert.Same (first, manager.GetObject (appId, typeof (RegisteredProbe)));

				Assert.Throws<InvalidOperationException> (
					() => manager.CreateObject (appId, typeof (RegisteredProbe), "/managed", SamplePath, true));

				ApplicationInfo info = Assert.Single (manager.GetRunningApplications (), a => a.ID == appId);
				Assert.Equal ("/managed", info.VirtualPath.TrimEnd ('/'));

				manager.StopObject (appId, typeof (RegisteredProbe));
				Assert.True (first.Stopped);
				Assert.Null (manager.GetObject (appId, typeof (RegisteredProbe)));
			} finally {
				manager.ShutdownApplication (appId);
			}

			Assert.DoesNotContain (manager.GetRunningApplications (), a => a.ID == appId);
		}
	}
}
