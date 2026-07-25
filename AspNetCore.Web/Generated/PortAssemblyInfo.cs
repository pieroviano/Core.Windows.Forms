//
// Assembly-level attributes added by the port. The upstream attributes (including the 44
// [assembly: WebResource] declarations that WebResource.axd depends on) still come from
// mono/mcs/class/System.Web/Assembly/AssemblyInfo.cs; only the [TypeForwardedTo] entries there are
// patched out, because the Membership types they forwarded to System.Web.ApplicationServices are
// now defined in this assembly.
//

using System.Runtime.CompilerServices;

// The Kestrel host has to reach PortPaths (application paths) and BareApplicationHost (the
// registered-object registry) to initialise the runtime. Both are internal by design - they are
// port plumbing, not public API.
// The port is strong-named (SignKey.snk, see Directory.Build.props), and a signed
// assembly may only befriend another signed assembly named with its public key.
[assembly: InternalsVisibleTo ("Core.Web.Hosting.Kestrel, PublicKey=002400000480000094000000060200000024000052534131000400000100010041c23647581173cd64456781bdeca5d2d95f82d27af80d3ba54c1e34900913dc1f79062d3acbfc1fba568eb7a835d3f734282936d2bd45ab2fc29aff390fe59822ec16377170fdcc957e30e85f76bd7ad63a2fbbce94653498e10e91a9b0fc570b7ccf8a5aca0c32630ba2fefea28e61c4438cf7231f37ba35f62bff56d0bbf0")]
