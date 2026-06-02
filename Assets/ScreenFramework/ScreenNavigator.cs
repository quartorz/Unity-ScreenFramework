using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

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

		/// <summary>
		/// 指定 Presenter のエントリを所属レイヤーから閉じる。
		/// どのレイヤーに属しているか呼び出し側が知らなくて済む。
		/// 全レイヤーに対して <see cref="IScreenNavigator.Close"/> を呼び、見つからないものは no-op。
		/// </summary>
		public static UniTask Close(IScreenPresenter target, PopOptions opt = default, CancellationToken ct = default)
		{
			if (target == null) throw new ArgumentNullException(nameof(target));
			var tasks = new List<UniTask>(3);
			foreach (var nav in All) tasks.Add(nav.Close(target, opt, ct));
			return UniTask.WhenAll(tasks);
		}

		/// <summary>
		/// 全レイヤーを上から（SystemDialog → Dialog → Page）順に検索し、
		/// <typeparamref name="TPresenter"/> 型のエントリを返す。なければ null。
		/// </summary>
		public static IScreenEntry FindEntry<TPresenter>() where TPresenter : class, IScreenPresenter
		{
			if (SystemDialog != null) { var e = SystemDialog.FindEntry<TPresenter>(); if (e != null) return e; }
			if (Dialog       != null) { var e = Dialog      .FindEntry<TPresenter>(); if (e != null) return e; }
			if (Page         != null) { var e = Page        .FindEntry<TPresenter>(); if (e != null) return e; }
			return null;
		}
	}
}
