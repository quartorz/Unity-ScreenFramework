using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// 1 回の遷移操作の文脈を表す。Presenter / Effect が共通して参照し、
	/// 操作種別、from/to の Identifier、画面間データ、stage signal の publish/wait を提供する。
	/// 遷移完了時に破棄され、後続の遷移には別インスタンスが渡る（sticky な stage は持ち越されない）。
	/// </summary>
	public interface ITransitionContext
	{
		OperationKind Kind { get; }

		/// <summary>
		/// 直接 displaced（Push なら下に隠れる現在の画面、Pop なら閉じる画面）の Identifier。
		/// 初期化時に対象が無いケース（最初の Push 等）では null。
		/// </summary>
		IScreenIdentifier From { get; }

		/// <summary>
		/// 直接 revealed（Push なら新画面、Pop なら下から現れる画面）の Identifier。
		/// Pop で revealed が無い（履歴 1 枚で Close）等の場合は null。
		/// </summary>
		IScreenIdentifier To { get; }

		INavigationDataReader Reader { get; }
		INavigationDataWriter Writer { get; }

		/// <summary>
		/// stage signal を publish する。sticky で、遷移完了まで残り、後続の Wait は即解決する。
		/// 同じ stage を複数回 publish しても害はない（最初の 1 回だけが効く）。
		/// </summary>
		void PublishStage<TStage>() where TStage : IStageKey;

		/// <summary>
		/// 指定 stage が publish されるまで待つ。すでに publish 済みなら即返す。
		/// 複数の waiter は broadcast される。
		/// <paramref name="timeout"/> 省略時は無限待ち（framework デフォルトは持たない）。
		/// </summary>
		UniTask WaitForStage<TStage>(CancellationToken ct = default, TimeSpan? timeout = null) where TStage : IStageKey;
	}
}
