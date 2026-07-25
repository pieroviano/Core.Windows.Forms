//
// Roslyn replacement for CodeDomProvider's compilation half (plan P2).
//
// The .aspx pipeline is untouched: AspGenerator -> TemplateParser -> TemplateControlCompiler
// still produce a CodeCompileUnit, and CSharpCodeProvider.GenerateCodeFromCompileUnit still turns
// that into C# source (verified working on .NET 8 - only the *compile* half of CodeDomProvider
// throws PlatformNotSupportedException). This class supplies that missing half:
//
//     CodeCompileUnit -> generated .cs on disk -> CSharpCompilation -> .dll -> Assembly
//
// Everything is expressed as CompilerParameters in / CompilerResults out so the upstream callers
// (CachingCompiler, AssemblyBuilder) keep their shape and HttpCompileException keeps rendering
// real file/line diagnostics.
//

using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.Text;
// Three names collide inside this namespace and all must be aliased:
//   * System.Web.Compilation declares its own Location (the .aspx parser's source location);
//   * "Compilation" unqualified binds to the enclosing System.Web.Compilation NAMESPACE, not to
//     Microsoft.CodeAnalysis.Compilation;
//   * LanguageVersion is declared by both Roslyn language namespaces.
using RoslynLocation = Microsoft.CodeAnalysis.Location;
using RoslynCompilation = Microsoft.CodeAnalysis.Compilation;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;
using VisualBasicLanguageVersion = Microsoft.CodeAnalysis.VisualBasic.LanguageVersion;

namespace System.Web.Compilation
{
	static class RoslynCompiler
	{
		static readonly object load_lock = new object ();
		static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly> (StringComparer.OrdinalIgnoreCase);
		static readonly Encoding source_encoding = new UTF8Encoding (true);

		// Cache of framework reference metadata - reading 170-odd reference assemblies off disk on
		// every page compile is pure waste, and MetadataReference instances are immutable.
		static readonly Dictionary<string, MetadataReference> reference_cache =
			new Dictionary<string, MetadataReference> (StringComparer.OrdinalIgnoreCase);

		public static CompilerResults CompileAssemblyFromDom (CodeDomProvider provider,
								     CompilerParameters options,
								     params CodeCompileUnit [] compilationUnits)
		{
			if (provider == null)
				throw new ArgumentNullException ("provider");
			if (options == null)
				throw new ArgumentNullException ("options");
			if (compilationUnits == null || compilationUnits.Length == 0)
				throw new ArgumentException ("no compilation units", "compilationUnits");

			string dir = TempDirectory (options);
			List<string> files = new List<string> (compilationUnits.Length);
			CodeGeneratorOptions genOptions = new CodeGeneratorOptions ();

			// The provider decides the language: "cs" for C#, "vb" for VB. The extension has to
			// follow, because CompileAssemblyFromFile infers the language from it.
			string extension = provider.FileExtension;
			if (String.IsNullOrEmpty (extension))
				extension = "cs";
			extension = extension.TrimStart ('.');

			for (int i = 0; i < compilationUnits.Length; i++) {
				string file = Path.Combine (dir, String.Format (CultureInfo.InvariantCulture, "{0}_{1}.{2}",
										BaseName (options), i, extension));
				using (StreamWriter writer = new StreamWriter (file, false, source_encoding))
					provider.GenerateCodeFromCompileUnit (compilationUnits [i], writer, genOptions);
				files.Add (file);
				// The generated source is a build artefact, but keep it when the app asked for
				// debug info - it is what the debugger and the error page point at.
				if (!options.IncludeDebugInformation)
					options.TempFiles.AddFile (file, false);
			}

			return CompileAssemblyFromFile (options, files.ToArray ());
		}

		public static CompilerResults CompileAssemblyFromFile (CompilerParameters options, params string [] fileNames)
		{
			if (options == null)
				throw new ArgumentNullException ("options");

			// An assembly with no source files is legitimate here and must not be rejected:
			// AppResourcesAssemblyBuilder.BuildDefaultAssembly compiles App_LocalResources into an
			// assembly that is nothing but embedded .resources. Upstream's AssemblyBuilder allows
			// exactly this (AssemblyBuilder.cs:760 bails out only when sources AND resources are all
			// empty), and Roslyn is happy to emit a compilation with no syntax trees.
			if ((fileNames == null || fileNames.Length == 0) &&
			    options.EmbeddedResources.Count == 0 && options.LinkedResources.Count == 0)
				throw new ArgumentException ("no source files and no resources", "fileNames");

			if (fileNames == null)
				fileNames = new string [0];

			bool isVisualBasic = IsVisualBasic (fileNames);

			List<SyntaxTree> trees = new List<SyntaxTree> (fileNames.Length);
			CompilerResults results = new CompilerResults (options.TempFiles);

			foreach (string file in fileNames) {
				SourceText text;
				try {
					using (FileStream fs = File.OpenRead (file))
						text = SourceText.From (fs, canBeEmbedded: options.IncludeDebugInformation);
				} catch (IOException e) {
					results.Errors.Add (new CompilerError (file, 0, 0, "CS1504", e.Message));
					results.NativeCompilerReturnValue = 1;
					return results;
				}
				// Passing the real path makes #line directives in the generated source resolve
				// back to the .aspx, which is what CompilationException reports to the user.
				trees.Add (ParseSource (text, file, isVisualBasic, options));
			}

			return CompileTrees (options, trees, results, isVisualBasic);
		}

		public static CompilerResults CompileAssemblyFromSource (CompilerParameters options, params string [] sources)
		{
			if (options == null)
				throw new ArgumentNullException ("options");
			if (sources == null || sources.Length == 0)
				throw new ArgumentException ("no sources", "sources");

			CSharpParseOptions parseOptions = new CSharpParseOptions (
				languageVersion: CSharpLanguageVersion.Latest,
				documentationMode: DocumentationMode.None,
				kind: SourceCodeKind.Regular,
				preprocessorSymbols: PreprocessorSymbols (options));

			List<SyntaxTree> trees = new List<SyntaxTree> (sources.Length);
			foreach (string src in sources)
				trees.Add (CSharpSyntaxTree.ParseText (SourceText.From (src, source_encoding), parseOptions));

			return CompileTrees (options, trees, new CompilerResults (options.TempFiles), false);
		}

		//
		// Language selection. ASP.NET picks the compiler from the page's Language= directive (or
		// <compilation defaultLanguage=>), which reaches here as the CodeDomProvider that generated
		// the source - so the file extension it produced is the authoritative signal.
		//
		static bool IsVisualBasic (string [] fileNames)
		{
			foreach (string file in fileNames) {
				if (String.Equals (Path.GetExtension (file), ".vb", StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		static SyntaxTree ParseSource (SourceText text, string path, bool isVisualBasic, CompilerParameters options)
		{
			if (isVisualBasic) {
				var vbParse = new VisualBasicParseOptions (
					languageVersion: VisualBasicLanguageVersion.Latest,
					documentationMode: DocumentationMode.None,
					kind: SourceCodeKind.Regular,
					preprocessorSymbols: VisualBasicPreprocessorSymbols (options));
				return VisualBasicSyntaxTree.ParseText (text, vbParse, path: path);
			}

			var csParse = new CSharpParseOptions (
				languageVersion: CSharpLanguageVersion.Latest,
				documentationMode: DocumentationMode.None,
				kind: SourceCodeKind.Regular,
				preprocessorSymbols: PreprocessorSymbols (options));
			return CSharpSyntaxTree.ParseText (text, csParse, path: path);
		}

		static CompilerResults CompileTrees (CompilerParameters options, List<SyntaxTree> trees,
						     CompilerResults results, bool isVisualBasic)
		{
			string outputAssembly = options.OutputAssembly;
			if (String.IsNullOrEmpty (outputAssembly)) {
				outputAssembly = Path.Combine (TempDirectory (options),
							       BaseName (options) + ".dll");
				options.OutputAssembly = outputAssembly;
			}

			string outputDir = Path.GetDirectoryName (outputAssembly);
			if (!String.IsNullOrEmpty (outputDir) && !Directory.Exists (outputDir))
				Directory.CreateDirectory (outputDir);

			CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions (
				OutputKind.DynamicallyLinkedLibrary,
				optimizationLevel: options.IncludeDebugInformation ? OptimizationLevel.Debug : OptimizationLevel.Release,
				allowUnsafe: true,
				warningLevel: options.WarningLevel < 0 ? 4 : options.WarningLevel,
				generalDiagnosticOption: ReportDiagnostic.Default,
				// Generated page classes routinely shadow base members and leave locals unused;
				// the upstream csc invocation suppressed the same set.
				specificDiagnosticOptions: SuppressedDiagnostics,
				deterministic: true,
				assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);

			RoslynCompilation compilation;
			if (isVisualBasic) {
				compilation = VisualBasicCompilation.Create (
					Path.GetFileNameWithoutExtension (outputAssembly),
					trees,
					ResolveReferences (options),
					VisualBasicOptions (options));
			} else {
				compilation = CSharpCompilation.Create (
					Path.GetFileNameWithoutExtension (outputAssembly),
					trees,
					ResolveReferences (options),
					compilationOptions);
			}

			string pdbPath = options.IncludeDebugInformation
				? Path.ChangeExtension (outputAssembly, ".pdb")
				: null;

			// CompilerParameters.EmbeddedResources must be honoured, not just the source files:
			// AppResourcesAssemblyBuilder compiles App_GlobalResources/App_LocalResources by handing
			// csc a set of .resources files this way. Ignoring them produced an assembly with a
			// ResourceManager and no resources, failing at runtime with
			// "Could not find the resource ... among the resources ''".
			List<ResourceDescription> manifestResources = ManifestResources (options);

			EmitResult emitResult;
			using (FileStream dll = File.Create (outputAssembly)) {
				if (pdbPath != null) {
					using (FileStream pdb = File.Create (pdbPath))
						emitResult = compilation.Emit (dll, pdb, manifestResources: manifestResources,
							options: new EmitOptions (
								debugInformationFormat: DebugInformationFormat.PortablePdb));
				} else
					emitResult = compilation.Emit (dll, manifestResources: manifestResources);
			}

			foreach (Diagnostic d in emitResult.Diagnostics) {
				if (d.Severity == DiagnosticSeverity.Hidden)
					continue;
				if (d.Severity == DiagnosticSeverity.Info)
					continue;
				results.Errors.Add (ToCompilerError (d));
			}

			if (!emitResult.Success) {
				results.NativeCompilerReturnValue = 1;
				try {
					File.Delete (outputAssembly);
				} catch (IOException) {
					// leaving a partial assembly behind is harmless; it is never loaded
				}
				return results;
			}

			results.NativeCompilerReturnValue = 0;
			results.PathToAssembly = outputAssembly;
			results.CompiledAssembly = LoadAssembly (outputAssembly);
			return results;
		}

		//
		// Generated page assemblies must share type identity with this assembly (a page's base
		// class is System.Web.UI.Page) and with the application's own assemblies, so they load
		// into the default context. A collectible context would give us unload-on-recompile but
		// would also break that identity, so recompilation of a changed page produces a new
		// assembly rather than replacing one - the same thing ASP.NET did per AppDomain.
		//
		static Assembly LoadAssembly (string path)
		{
			lock (load_lock) {
				Assembly asm;
				if (loaded.TryGetValue (path, out asm))
					return asm;

				asm = AssemblyLoadContext.Default.LoadFromAssemblyPath (Path.GetFullPath (path));
				loaded [path] = asm;
				return asm;
			}
		}

		//
		// csc's /resource: (embedded, carried in this assembly) and /linkresource: (referenced by
		// name, left on disk). The manifest name is the bare file name, which is what
		// AppResourcesAssemblyBuilder expects: it names its .resources files after the resource
		// namespace, and ResourceManager then looks them up by exactly that.
		//
		static List<ResourceDescription> ManifestResources (CompilerParameters options)
		{
			var result = new List<ResourceDescription> ();

			foreach (string path in options.EmbeddedResources) {
				if (String.IsNullOrEmpty (path) || !File.Exists (path))
					continue;
				string name = Path.GetFileName (path);
				string captured = path;
				result.Add (new ResourceDescription (name, () => File.OpenRead (captured), isPublic: true));
			}

			foreach (string path in options.LinkedResources) {
				if (String.IsNullOrEmpty (path) || !File.Exists (path))
					continue;
				string name = Path.GetFileName (path);
				string captured = path;
				// Roslyn has no linked-resource concept in Emit; embedding keeps the resource
				// reachable, which is what every caller here actually wants.
				result.Add (new ResourceDescription (name, () => File.OpenRead (captured), isPublic: true));
			}

			return result;
		}

		static CompilerError ToCompilerError (Diagnostic d)
		{
			CompilerError error = new CompilerError ();
			error.ErrorNumber = d.Id;
			error.ErrorText = d.GetMessage (CultureInfo.CurrentCulture);
			error.IsWarning = d.Severity != DiagnosticSeverity.Error;

			RoslynLocation location = d.Location;
			if (location != null && location.IsInSource) {
				FileLinePositionSpan span = location.GetMappedLineSpan ();
				error.FileName = span.Path ?? String.Empty;
				// Roslyn positions are 0-based; CompilerError is 1-based, as csc reported.
				error.Line = span.StartLinePosition.Line + 1;
				error.Column = span.StartLinePosition.Character + 1;
			} else
				error.FileName = String.Empty;

			return error;
		}

		//
		// Reference resolution. CompilerParameters.ReferencedAssemblies is a mix of full paths
		// (added by CachingCompiler.GetExtraAssemblies from Assembly.Location) and bare display
		// names (added from <compilation><assemblies> in web.config). Both must resolve, and the
		// framework reference set has to be added because nothing in the upstream tree knows it
		// needs to.
		//
		//
		// Assemblies this port REPLACES. They must never reach the compile reference set, or every
		// generated page that touches one of their types fails with CS0433 "exists in both".
		//
		// build/WebFormsPort.targets removes these at build time, but the runtime compiler builds its
		// own reference set from the trusted platform assemblies and everything loaded in the process,
		// so it has to apply the same rule. Found via the .asmx help page, which uses HttpUtility -
		// but it would equally have hit any application page written against HttpUtility or
		// ConfigurationManager.
		//
		static readonly HashSet<string> ReplacedAssemblies = new HashSet<string> (StringComparer.OrdinalIgnoreCase) {
			"System.Web",                                 // empty framework facade; Core.Web is the real one
			"System.Web.HttpUtility",                     // declares System.Web.HttpUtility / IHtmlString
			"System.Configuration",                       // empty framework facade
			"System.Configuration.ConfigurationManager",  // Core.Configuration is the real one
		};

		static IEnumerable<MetadataReference> ResolveReferences (CompilerParameters options)
		{
			Dictionary<string, MetadataReference> byName =
				new Dictionary<string, MetadataReference> (StringComparer.OrdinalIgnoreCase);

			// 1. the framework reference set this process was built against
			foreach (string path in TrustedPlatformAssemblies ()) {
				string simple = Path.GetFileNameWithoutExtension (path);
				if (ReplacedAssemblies.Contains (simple))
					continue;
				if (!byName.ContainsKey (simple)) {
					MetadataReference r = CachedReference (path);
					if (r != null)
						byName [simple] = r;
				}
			}

			// 2. everything already loaded that has a file backing it - this is what pulls in
			//    System.Web itself plus the application's bin assemblies
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies ()) {
				if (asm.IsDynamic)
					continue;
				string location;
				try {
					location = asm.Location;
				} catch (NotSupportedException) {
					continue;
				}
				if (String.IsNullOrEmpty (location) || !File.Exists (location))
					continue;
				string simple = Path.GetFileNameWithoutExtension (location);
				if (ReplacedAssemblies.Contains (simple))
					continue;
				if (byName.ContainsKey (simple))
					continue;
				MetadataReference r = CachedReference (location);
				if (r != null)
					byName [simple] = r;
			}

			// 3. explicit references from the caller (these win over the defaults above)
			foreach (string entry in options.ReferencedAssemblies) {
				if (String.IsNullOrEmpty (entry))
					continue;

				string path = ResolveReferencePath (entry);
				if (path == null)
					continue;
				// An application's <compilation><assemblies> may still name System.Web etc.; those
				// entries must not drag the replaced framework assembly back in.
				if (ReplacedAssemblies.Contains (Path.GetFileNameWithoutExtension (path)))
					continue;

				MetadataReference r = CachedReference (path);
				if (r != null)
					byName [Path.GetFileNameWithoutExtension (path)] = r;
			}

			return byName.Values.ToArray ();
		}

		static string ResolveReferencePath (string entry)
		{
			if (File.Exists (entry))
				return Path.GetFullPath (entry);

			// a display name such as "System.Data, Version=4.0.0.0, Culture=neutral, ..."
			string simple = entry;
			int comma = simple.IndexOf (',');
			if (comma > 0)
				simple = simple.Substring (0, comma).Trim ();
			if (simple.EndsWith (".dll", StringComparison.OrdinalIgnoreCase))
				simple = simple.Substring (0, simple.Length - 4);

			foreach (string path in TrustedPlatformAssemblies ()) {
				if (String.Equals (Path.GetFileNameWithoutExtension (path), simple, StringComparison.OrdinalIgnoreCase))
					return path;
			}

			// last resort: let the loader find it, then use where it came from
			try {
				Assembly asm = Assembly.Load (new AssemblyName (simple));
				if (asm != null && !String.IsNullOrEmpty (asm.Location))
					return asm.Location;
			} catch {
				// an unresolvable <add assembly="..."/> is not fatal; the compile will report
				// the resulting CS0246 against the real source line instead
			}

			return null;
		}

		static MetadataReference CachedReference (string path)
		{
			lock (reference_cache) {
				MetadataReference reference;
				if (reference_cache.TryGetValue (path, out reference))
					return reference;
				try {
					reference = MetadataReference.CreateFromFile (path);
				} catch (IOException) {
					return null;
				} catch (BadImageFormatException) {
					return null;
				}
				reference_cache [path] = reference;
				return reference;
			}
		}

		//
		// VB compilation options.
		//
		// ASP.NET's defaults are NOT Roslyn's: a WebForms page compiles with Option Strict Off and
		// Option Explicit On (<compilation strict="false" explicit="true">, which is the schema
		// default). Getting Strict wrong is not cosmetic - VB pages written against late binding, or
		// relying on implicit narrowing conversions, simply fail to compile under Option Strict On.
		//
		// <compilation strict= explicit=> reaches us through CompilerParameters.CompilerOptions as
		// csc/vbc switches, so those are honoured when present and otherwise fall back to the
		// ASP.NET defaults.
		//
		static VisualBasicCompilationOptions VisualBasicOptions (CompilerParameters options)
		{
			bool strict = SwitchValue (options.CompilerOptions, "optionstrict", false);
			bool explicitOn = SwitchValue (options.CompilerOptions, "optionexplicit", true);
			bool infer = SwitchValue (options.CompilerOptions, "optioninfer", true);

			return new VisualBasicCompilationOptions (
				OutputKind.DynamicallyLinkedLibrary,
				optimizationLevel: options.IncludeDebugInformation ? OptimizationLevel.Debug : OptimizationLevel.Release,
				optionStrict: strict ? OptionStrict.On : OptionStrict.Off,
				optionExplicit: explicitOn,
				optionInfer: infer,
				// Microsoft.VisualBasic.Core ships in the framework and is on the reference set, so
				// the VB runtime helpers do not need embedding.
				embedVbCoreRuntime: false,
				// The generated page emits its own Imports from <pages><namespaces>, so no global
				// imports are added here - adding them would change name resolution in user code.
				rootNamespace: String.Empty,
				generalDiagnosticOption: ReportDiagnostic.Default,
				specificDiagnosticOptions: SuppressedVisualBasicDiagnostics,
				deterministic: true,
				assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);
		}

		// Reads a vbc-style boolean switch: /optionstrict+, /optionstrict-, /optionstrict:custom.
		static bool SwitchValue (string compilerOptions, string name, bool fallback)
		{
			if (String.IsNullOrEmpty (compilerOptions))
				return fallback;

			foreach (string token in compilerOptions.Split (new [] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries)) {
				string t = token.TrimStart ('/', '-');
				if (!t.StartsWith (name, StringComparison.OrdinalIgnoreCase))
					continue;

				string rest = t.Substring (name.Length);
				if (rest.Length == 0 || rest == "+")
					return true;
				if (rest == "-")
					return false;
				if (rest.StartsWith (":", StringComparison.Ordinal)) {
					string v = rest.Substring (1);
					// "custom" means warnings rather than errors - closer to Off than On.
					if (String.Equals (v, "custom", StringComparison.OrdinalIgnoreCase))
						return false;
					return String.Equals (v, "on", StringComparison.OrdinalIgnoreCase) ||
					       String.Equals (v, "true", StringComparison.OrdinalIgnoreCase);
				}
			}

			return fallback;
		}

		// VB conditional compilation symbols are name/value pairs, unlike C#'s bare names.
		static IEnumerable<KeyValuePair<string, object>> VisualBasicPreprocessorSymbols (CompilerParameters options)
		{
			var symbols = new List<KeyValuePair<string, object>> ();
			foreach (string name in PreprocessorSymbols (options))
				symbols.Add (new KeyValuePair<string, object> (name, true));
			return symbols;
		}

		static readonly KeyValuePair<string, ReportDiagnostic> [] SuppressedVisualBasicDiagnostics = {
			new KeyValuePair<string, ReportDiagnostic> ("BC42024", ReportDiagnostic.Suppress), // unused local
			new KeyValuePair<string, ReportDiagnostic> ("BC42025", ReportDiagnostic.Suppress), // access of shared member via instance
			new KeyValuePair<string, ReportDiagnostic> ("BC40008", ReportDiagnostic.Suppress), // obsolete
			new KeyValuePair<string, ReportDiagnostic> ("BC40000", ReportDiagnostic.Suppress), // obsolete
			new KeyValuePair<string, ReportDiagnostic> ("BC42104", ReportDiagnostic.Suppress), // variable used before assigned
		};

		static string [] tpa;

		static string [] TrustedPlatformAssemblies ()
		{
			if (tpa != null)
				return tpa;

			string data = AppContext.GetData ("TRUSTED_PLATFORM_ASSEMBLIES") as string;
			if (String.IsNullOrEmpty (data)) {
				tpa = new string [0];
				return tpa;
			}

			tpa = data.Split (Path.PathSeparator)
				.Where (p => p.Length > 0 && p.EndsWith (".dll", StringComparison.OrdinalIgnoreCase))
				.ToArray ();
			return tpa;
		}

		static IEnumerable<string> PreprocessorSymbols (CompilerParameters options)
		{
			List<string> symbols = new List<string> ();
			if (options.IncludeDebugInformation)
				symbols.Add ("DEBUG");
			symbols.Add ("TRACE");

			// CompilerOptions carries the raw csc switches from <compilation><compilers>. Only
			// /d:/define: is meaningful to us; the rest (/optimize, /warnaserror, ...) is already
			// covered by CSharpCompilationOptions above.
			string raw = options.CompilerOptions;
			if (String.IsNullOrEmpty (raw))
				return symbols;

			foreach (string token in raw.Split (new [] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) {
				string t = token.TrimStart ('/', '-');
				int colon = t.IndexOf (':');
				if (colon < 0)
					continue;
				string name = t.Substring (0, colon);
				if (!String.Equals (name, "d", StringComparison.OrdinalIgnoreCase) &&
				    !String.Equals (name, "define", StringComparison.OrdinalIgnoreCase))
					continue;
				foreach (string sym in t.Substring (colon + 1).Split (new [] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
					symbols.Add (sym.Trim ());
			}

			return symbols;
		}

		static readonly KeyValuePair<string, ReportDiagnostic> [] SuppressedDiagnostics = {
			new KeyValuePair<string, ReportDiagnostic> ("CS0169", ReportDiagnostic.Suppress), // unused field
			new KeyValuePair<string, ReportDiagnostic> ("CS0219", ReportDiagnostic.Suppress), // assigned but never used
			new KeyValuePair<string, ReportDiagnostic> ("CS0414", ReportDiagnostic.Suppress), // assigned but never used
			new KeyValuePair<string, ReportDiagnostic> ("CS0649", ReportDiagnostic.Suppress), // never assigned
			new KeyValuePair<string, ReportDiagnostic> ("CS0108", ReportDiagnostic.Suppress), // hides inherited member
			new KeyValuePair<string, ReportDiagnostic> ("CS0114", ReportDiagnostic.Suppress), // hides inherited, add override
			new KeyValuePair<string, ReportDiagnostic> ("CS0618", ReportDiagnostic.Suppress), // obsolete
			new KeyValuePair<string, ReportDiagnostic> ("CS0612", ReportDiagnostic.Suppress), // obsolete, no message
		};

		static string TempDirectory (CompilerParameters options)
		{
			string dir = options.TempFiles != null ? options.TempFiles.TempDir : null;
			if (String.IsNullOrEmpty (dir))
				dir = System.Web.Util.PortPaths.DynamicBase;
			if (!Directory.Exists (dir))
				Directory.CreateDirectory (dir);
			return dir;
		}

		static string BaseName (CompilerParameters options)
		{
			if (!String.IsNullOrEmpty (options.OutputAssembly))
				return Path.GetFileNameWithoutExtension (options.OutputAssembly);
			return "App_Web_" + Guid.NewGuid ().ToString ("N").Substring (0, 8);
		}
	}
}
