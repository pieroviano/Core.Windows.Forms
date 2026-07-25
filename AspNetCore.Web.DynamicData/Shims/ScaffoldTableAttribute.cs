//
// ScaffoldTableAttribute, which .NET Core never carried.
//
// On .NET Framework this lived in System.ComponentModel.DataAnnotations. The modern replacement
// package, System.ComponentModel.Annotations, kept ScaffoldColumnAttribute and dropped this one -
// scaffolding a whole TABLE was a Dynamic Data concept, and Dynamic Data was not ported, so the
// attribute went with it.
//
// It is declared here, in its original namespace, so a ported application's
// [ScaffoldTable(true)] on an entity class keeps compiling and keeps meaning what it meant. Nothing
// else in the framework declares the name, so there is no collision to worry about - the same reason
// the port can declare it at all.
//

using System;

namespace System.ComponentModel.DataAnnotations
{
	/// <summary>
	/// Whether Dynamic Data scaffolds this table. Put it on an entity class to include or exclude it
	/// from the generated list, detail, edit and insert pages.
	/// </summary>
	[AttributeUsage (AttributeTargets.Class, AllowMultiple = false)]
	public sealed class ScaffoldTableAttribute : Attribute
	{
		public ScaffoldTableAttribute (bool scaffold)
		{
			Scaffold = scaffold;
		}

		public bool Scaffold { get; }
	}
}
