//
// AssemblyInfo.cs
//
// Author:
//   Lluis Sanchez Gual (lluis@novell.com)
//
// (C) 2005 Novell, Inc.  http://www.novell.com
//

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Diagnostics;
using System.Reflection;
using System.Resources;
using System.Security;
using System.Security.Permissions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about the System.Configuration (.Net 2.0 only) assembly

[assembly: AssemblyTitle ("System.Configuration.dll")]
[assembly: AssemblyDescription ("System.Configuration.dll")]
[assembly: AssemblyDefaultAlias ("System.Configuration.dll")]

[assembly: AssemblyCompany (Consts.MonoCompany)]
[assembly: AssemblyProduct (Consts.MonoProduct)]
[assembly: AssemblyCopyright (Consts.MonoCopyright)]
[assembly: AssemblyVersion (Consts.FxVersion)]
[assembly: SatelliteContractVersion (Consts.FxVersion)]
[assembly: AssemblyInformationalVersion (Consts.FxFileVersion)]

[assembly: CLSCompliant (true)]
[assembly: NeutralResourcesLanguage ("en-US")]

[assembly: ComVisible (false)]
[assembly: AllowPartiallyTrustedCallers]

	[assembly: AssemblyDelaySign (true)]

[assembly: InternalsVisibleTo ("Core.Web, PublicKey=002400000480000094000000060200000024000052534131000400000100010041c23647581173cd64456781bdeca5d2d95f82d27af80d3ba54c1e34900913dc1f79062d3acbfc1fba568eb7a835d3f734282936d2bd45ab2fc29aff390fe59822ec16377170fdcc957e30e85f76bd7ad63a2fbbce94653498e10e91a9b0fc570b7ccf8a5aca0c32630ba2fefea28e61c4438cf7231f37ba35f62bff56d0bbf0")]
[assembly: AssemblyFileVersion (Consts.FxFileVersion)]
[assembly: ComCompatibleVersion (1, 0, 3300, 0)]
