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

			// 既に初期化済みのまま再初期化すると旧 navigator の画面群が孤児化し、pending awaiter が永久未解決になる。
			// 明示的に await ScreenNavigator.Shutdown() を呼んでから初期化させる。
			if (Page != null || Dialog != null || SystemDialog != null)
				throw new InvalidOperationException(
					"ScreenNavigator は既に初期化済みです。再初期化の前に await ScreenNavigator.Shutdown() を呼んでください。");

			// 全レイヤーをローカルに組み立ててから一括代入する。途中の ScreenNavigatorImpl ctor が
			// 検証(Container 欠落など)で throw しても、static 参照に部分状態を残さない
			// (= 失敗した Initialize の後、Shutdown を挟まず正しい設定で再 Initialize できる)。
			var page = new ScreenNavigatorImpl(services, setup.Page);
			var dialog = new ScreenNavigatorImpl(services, setup.Dialog);
			var systemDialog = new ScreenNavigatorImpl(services, setup.SystemDialog);

			Page = page;
			Dialog = dialog;
			SystemDialog = systemDialog;
		}

		/// <summary>
		/// 全レイヤーを破棄する。各 navigator を <see cref="IScreenNavigator.DismissAll"/> で
		/// 退場演出付きに畳み（進行中の遷移は完了を待ってから、待機中の遷移と pending な
		/// <see cref="IScreenNavigator.PushAndAwait{TResult}"/> の awaiter は
		/// <see cref="OperationCanceledException"/> で解決される）、静的参照を null に戻す。
		/// シーン破棄や再初期化の前に呼ぶ。呼び出し後は再度 <see cref="Initialize"/> が必要。
		/// <para>静的参照は同期的に null にするので、戻り値を待たずとも直後の <see cref="Initialize"/> は可能。
		/// 退場演出の完了まで待ちたい場合は戻り値を await する。</para>
		/// </summary>
		public static UniTask Shutdown()
		{
			var page = Page;
			var dialog = Dialog;
			var systemDialog = SystemDialog;

			// 先に静的参照を外して、畳んでいる最中に新しい操作が差し込まれないようにする。
			Page = null;
			Dialog = null;
			SystemDialog = null;

			var tasks = new List<UniTask>(3);
			if (page != null) tasks.Add(page.DismissAll());
			if (dialog != null) tasks.Add(dialog.DismissAll());
			if (systemDialog != null) tasks.Add(systemDialog.DismissAll());
			return UniTask.WhenAll(tasks);
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
