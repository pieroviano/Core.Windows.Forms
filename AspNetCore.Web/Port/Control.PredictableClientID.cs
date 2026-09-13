//
// ClientIDMode.Predictable, as ASP.NET 4 computes it.
//
// Upstream's GeneratePredictableClientID (System.Web.UI/Control.cs) diverges from ASP.NET in two ways
// that change the id of almost every control a real page renders, and with it every selector and every
// document.getElementById in the page's own script:
//
//   - only the Page ended the prefix chain. A MasterPage is a naming container too, so every control in a
//     content page rendered as "ctl00_MainContent_x"; ASP.NET renders "MainContent_x".
//   - a data item container (RepeaterItem, DataListItem, GridViewRow...) contributed its own automatic
//     id AND the row index: "rep_ctl03_pick_3". ASP.NET skips the item's id - that is what the index
//     suffix is for - and renders "rep_pick_3".
//
// This is referencesource/System.Web/UI/Control.cs GetPredictableClientIDPrefix/Suffix, expressed over
// upstream's fields. Tools/port-patches.txt points GetClientID's Predictable case here; the upstream
// method stays compiled and unreferenced, for comparison.
//

using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Web.UI.WebControls;

namespace System.Web.UI
{
	public partial class Control
	{
		string GeneratePredictableClientIDPort ()
		{
			string prefix = PredictableClientIDPrefix ();
			string suffix = PredictableClientIDSuffix ();

			if (!String.IsNullOrEmpty (suffix))
				prefix = String.IsNullOrEmpty (prefix) ? suffix.Substring (1) : prefix + suffix;

			return prefix ?? String.Empty;
		}

		string PredictableClientIDPrefix ()
		{
			Control namingContainer = NamingContainer;
			if (namingContainer == null)
				return _userId;

			EnsureIDInternal ();
			string id = _userId;

			// The page and the master page end the chain: neither contributes to a child's id.
			if (namingContainer is Page || namingContainer is MasterPage)
				return id;

			string prefix = namingContainer.GetClientID ();
			if (String.IsNullOrEmpty (prefix))
				return id;

			// A data item container is identified by the index suffix, not by its own (automatic) id.
			if (!String.IsNullOrEmpty (id) && (!(this is IDataItemContainer) || this is IDataBoundItemControl))
				return prefix + ClientIDSeparator + id;

			return prefix;
		}

		string PredictableClientIDSuffix ()
		{
			Control dataItemContainer = DataItemContainer;
			if (dataItemContainer == null || dataItemContainer is IDataBoundItemControl)
				return null;

			if (this is IDataItemContainer && !(this is IDataBoundItemControl))
				return null;

			IDataItemContainer item = (IDataItemContainer) dataItemContainer;
			IDataKeysControl keysControl = dataItemContainer.DataKeysContainer as IDataKeysControl;

			if (keysControl != null && keysControl.ClientIDRowSuffix != null && keysControl.ClientIDRowSuffix.Length > 0) {
				var suffix = new StringBuilder ();
				IOrderedDictionary values = keysControl.ClientIDRowSuffixDataKeys [item.DisplayIndex].Values;
				foreach (string name in keysControl.ClientIDRowSuffix)
					suffix.Append (ClientIDSeparator).Append (values [name]);
				return suffix.ToString ();
			}

			int index = item.DisplayIndex;
			return index >= 0 ? ClientIDSeparator + index.ToString (CultureInfo.InvariantCulture) : null;
		}
	}
}
