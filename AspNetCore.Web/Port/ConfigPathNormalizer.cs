//
// Normalises the virtual path that WebConfigurationManager.GetSection (name, path) resolves a
// configuration hierarchy for.
//
// Upstream passes the caller's path straight to OpenWebConfiguration, which only ever treats it as a
// DIRECTORY - and only recognises one that carries a trailing slash. Measured against
// Samples/WebFormsSample, whose Secure/ sub-directory has its own web.config denying anonymous users:
//
//     "/Secure/"             -> the sub-directory's config          (correct)
//     "/Secure"              -> the application root's config       (wrong)
//     "~/Secure"             -> the application root's config       (wrong)
//     "/Secure/Secret.aspx"  -> the application root's config       (wrong)
//
// The last of those is the form that matters: asking for a section "for this page" is the natural
// call, and it silently returned the wrong answer rather than failing. A caller checking
// <authorization> that way would conclude the page is public.
//
// So the path handed to OpenWebConfiguration is reduced to the directory that contains it. The
// caller's ORIGINAL path is still used for <location> lookups further down, which is the division
// upstream intended: the hierarchy comes from the directory, location sections from the full path.
//

using System;

namespace System.Web.Util
{
	static class ConfigPathNormalizer
	{
		/// <summary>
		/// The directory whose web.config governs <paramref name="virtualPath"/>, slash-terminated.
		/// </summary>
		public static string ToConfigDirectory (string virtualPath)
		{
			if (String.IsNullOrEmpty (virtualPath))
				return virtualPath;

			string path = virtualPath.Replace ('\\', '/');

			// "~/Secure" and "/Secure" describe the same place; the rest of the method only has to
			// deal with one of them.
			if (path.StartsWith ("~/", StringComparison.Ordinal))
				path = path.Substring (1);
			else if (path == "~")
				path = "/";

			// Already a directory.
			if (path.EndsWith ("/", StringComparison.Ordinal))
				return path;

			int lastSlash = path.LastIndexOf ('/');
			string lastSegment = lastSlash < 0 ? path : path.Substring (lastSlash + 1);

			// A final segment with an extension is a file - "/Secure/Secret.aspx" - and its
			// configuration is its directory's. Anything else is a directory given without the
			// trailing slash, so it only needs one added.
			//
			// An extension is the only signal available: this runs without a request, so there is
			// nothing to ask whether the path exists, and a directory legitimately may not exist on
			// disk while still carrying configuration through a <location> element.
			if (lastSegment.IndexOf ('.') >= 0)
				return lastSlash <= 0 ? "/" : path.Substring (0, lastSlash + 1);

			return path + "/";
		}
	}
}
