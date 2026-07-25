//
// The plan, and the findings that go with it.
//
// This type is the seam that keeps --apply honest. The planner produces a ConversionPlan and NOTHING
// else in the tool touches the filesystem; dry-run prints the plan, --apply executes the same object.
// The two modes therefore cannot disagree about what would happen, which is the only structural way to
// stop a preview drifting from the thing it previews.
//

using System;
using System.Collections.Generic;

namespace PortProject
{
	enum ActionKind
	{
		/// <summary>Rename the legacy project to *.old, so the conversion is undoable.</summary>
		Rename,

		/// <summary>Write a new file, or replace one whose original has been backed up.</summary>
		Write,

		/// <summary>Deliberately not done, with a reason - printed so silence is never the answer.</summary>
		Skip,
	}

	sealed class FileAction
	{
		public FileAction (ActionKind kind, string path, string reason, string content = null,
				   string destination = null)
		{
			Kind = kind;
			Path = path;
			Reason = reason;
			Content = content;
			Destination = destination;
		}

		public ActionKind Kind { get; }

		/// <summary>Absolute path this action is about.</summary>
		public string Path { get; }

		public string Reason { get; }

		/// <summary>Full text to write. Null for Rename and Skip.</summary>
		public string Content { get; }

		/// <summary>Target of a Rename. Null otherwise.</summary>
		public string Destination { get; }
	}

	enum Severity
	{
		/// <summary>Done, and here is why. Every inference the tool made is reported at this level.</summary>
		Info,

		/// <summary>Converted, but a human has to check it. Does not fail the run.</summary>
		NeedsAttention,

		/// <summary>Cannot be ported. Fails the run with exit code 2.</summary>
		Blocker,
	}

	sealed class Finding
	{
		public Finding (Severity severity, string title, string detail, string evidence = null,
				string guideSection = null)
		{
			Severity = severity;
			Title = title;
			Detail = detail;
			Evidence = evidence;
			GuideSection = guideSection;
		}

		public Severity Severity { get; }

		public string Title { get; }

		public string Detail { get; }

		/// <summary>Where the tool saw it - "MyApp.csproj:41", "Echo.svc". Null when not from a file.</summary>
		public string Evidence { get; }

		/// <summary>The PORTING-GUIDE.md section that explains what to do. Null when not applicable.</summary>
		public string GuideSection { get; }
	}

	sealed class ConversionPlan
	{
		readonly List<FileAction> actions = new List<FileAction> ();
		readonly List<Finding> findings = new List<Finding> ();

		public ConversionPlan (string projectPath, string applicationDirectory)
		{
			ProjectPath = projectPath;
			ApplicationDirectory = applicationDirectory;
		}

		public string ProjectPath { get; }

		public string ApplicationDirectory { get; }

		public IReadOnlyList<FileAction> Actions {
			get { return actions; }
		}

		public IReadOnlyList<Finding> Findings {
			get { return findings; }
		}

		/// <summary>True when nothing can be done and the run should stop.</summary>
		public bool HasBlockers {
			get { return findings.Exists (f => f.Severity == Severity.Blocker); }
		}

		public int NeedsAttentionCount {
			get { return findings.FindAll (f => f.Severity == Severity.NeedsAttention).Count; }
		}

		public void Add (FileAction action)
		{
			actions.Add (action);
		}

		public void Report (Severity severity, string title, string detail, string evidence = null,
				    string guideSection = null)
		{
			findings.Add (new Finding (severity, title, detail, evidence, guideSection));
		}
	}
}
