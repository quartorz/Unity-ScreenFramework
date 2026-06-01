using System;
using System.Collections.Generic;

namespace ScreenFramework
{
	public static class ScreenNavigator
	{
		public static IScreenNavigator Page { get; internal set; }
		public static IScreenNavigator Dialog { get; internal set; }
		public static IScreenNavigator SystemDialog { get; internal set; }

		internal static IEnumerable<IScreenNavigator> All
		{
			get
			{
				if (Page != null) yield return Page;
				if (Dialog != null) yield return Dialog;
				if (SystemDialog != null) yield return SystemDialog;
			}
		}

		public static void Initialize(ScreenServices services, ScreenLayerSetup setup)
		{
			if (services == null) throw new ArgumentNullException(nameof(services));
			if (setup == null) throw new ArgumentNullException(nameof(setup));
			if (setup.Page == null) throw new ArgumentException("Page layer config is required.", nameof(setup));
			if (setup.Dialog == null) throw new ArgumentException("Dialog layer config is required.", nameof(setup));
			if (setup.SystemDialog == null) throw new ArgumentException("SystemDialog layer config is required.", nameof(setup));

			Page = new ScreenNavigatorImpl(services, setup.Page);
			Dialog = new ScreenNavigatorImpl(services, setup.Dialog);
			SystemDialog = new ScreenNavigatorImpl(services, setup.SystemDialog);
		}

		/// <summary>テストでの差し替え等用。</summary>
		public static void Override(IScreenNavigator page = null, IScreenNavigator dialog = null, IScreenNavigator systemDialog = null)
		{
			if (page != null) Page = page;
			if (dialog != null) Dialog = dialog;
			if (systemDialog != null) SystemDialog = systemDialog;
		}
	}
}
