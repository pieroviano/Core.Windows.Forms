//
// System.Web.Util.ICalls - port override
//
// Upstream: mono/mcs/class/System.Web/System.Web.Util/ICalls.cs
//
// All three members were [MethodImpl (MethodImplOptions.InternalCall)] - implemented in the Mono
// runtime's C code. CoreCLR has no such entry points, and merely *loading* a type that declares one
// fails with SecurityException ("ECall methods must be packaged into a system module"), which is
// what turned every request into a 500 before this override existed.
//
// Managed equivalents:
//
//   GetMachineConfigPath        - the embedded machine.config that Port/MachineConfig.cs extracts.
//   GetMachineInstallDirectory  - on Mono this is the directory holding machine.config, which is
//                                 exactly what HttpRuntime and SimpleWorkerRequest use it for.
//   GetUnmanagedResourcesPtr    - Mono read an assembly's unmanaged resource blob to support the
//                                 literal-string-table optimisation. Its only caller is
//                                 TemplateControl.ReadStringResource, and generated pages never
//                                 reach it: TemplateControlCompiler declares the __stringResource
//                                 field (TemplateControlCompiler.cs:1724) but never assigns it, so
//                                 the SetStringResourcePointer call it emits gets null and returns
//                                 immediately. Returning false is therefore unreachable in practice;
//                                 if application code does call ReadStringResource directly it gets
//                                 a clear HttpException instead of a silently wrong pointer.
//

using System;
using System.IO;
using System.Reflection;

namespace System.Web.Util
{
	class ICalls
	{
		ICalls ()
		{
		}

		public static string GetMachineConfigPath ()
		{
			return PortPaths.MachineConfigPath;
		}

		public static string GetMachineInstallDirectory ()
		{
			string path = PortPaths.MachineConfigPath;
			if (String.IsNullOrEmpty (path))
				return AppContext.BaseDirectory;
			return Path.GetDirectoryName (path);
		}

		public static bool GetUnmanagedResourcesPtr (Assembly assembly, out IntPtr ptr, out int length)
		{
			ptr = IntPtr.Zero;
			length = 0;
			return false;
		}
	}
}
