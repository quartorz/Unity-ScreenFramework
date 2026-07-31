using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// Presenter のライフサイクル配線を集約する共通基底。
	/// <see cref="IScreenPresenter"/> の各 hook を explicit 実装し、protected virtual へ振り分ける。
	/// View(Input/Output) の取り込み方は派生（<see cref="ScreenPresenter{TInput,TOutput}"/> /
	/// <see cref="ComposedScreenPresenter{TInput,TOutput}"/>）が IScreenPresenter を再実装して決める。
	/// </summary>
	public abstract class ScreenPresenterBase<TInput, TOutput> : IScreenPresenter
		where TInput : class
		where TOutput : class
	{
		/// <summary>Navigator から注入される共通サービス。プロジェクト基底で型付きに細める想定。</summary>
		protected ScreenServices Services { get; private set; }

		void IScreenPresenter.AssignServices(ScreenServices services) => Services = services;

		UniTask IScreenPresenter.OnInitialize(CancellationToken ct) => OnInitialize(ct);

		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
			=> OnBeforeLoad(reader, ctx, ct);

		// 既定の OnAfterLoad は View を取り込まない。ScreenPresenter / ComposedScreenPresenter が
		// IScreenPresenter を再実装して View の扱い（In/Out 公開 or Compose）を差し替える。
		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance view, INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
			=> OnAfterLoad(reader, ctx, ct);

		UniTask IScreenPresenter.OnBeforeShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => OnBeforeShow(reader, ctx, ct);
		UniTask IScreenPresenter.OnAfterShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => OnAfterShow(reader, ctx, ct);
		UniTask IScreenPresenter.OnBeforeHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => OnBeforeHide(writer, ctx, ct);
		UniTask IScreenPresenter.OnAfterHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => OnAfterHide(writer, ctx, ct);
		UniTask IScreenPresenter.OnSuspend(CancellationToken ct) => OnSuspend(ct);
		UniTask IScreenPresenter.OnResume(CancellationToken ct) => OnResume(ct);
		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter writer, CancellationToken ct) => OnAfterUnload(writer, ct);

		/// <summary>Model の構築など、Services を要するインスタンス初期化用。AssignServices 後・OnBeforeLoad 前に一度だけ呼ばれる。</summary>
		protected virtual UniTask OnInitialize(CancellationToken ct) => UniTask.CompletedTask;

		protected virtual UniTask OnBeforeLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnAfterLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnBeforeShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnAfterShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnBeforeHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnAfterHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnSuspend(CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnResume(CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnAfterUnload(INavigationDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;
	}
}
