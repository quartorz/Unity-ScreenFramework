using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	public interface IScreenPresenter
	{
		/// <summary>
		/// インスタンス自身の組み立て用 hook。<see cref="AssignServices"/> 直後・OnBeforeLoad より前に
		/// インスタンスごとに必ず一度だけ呼ばれる。Model の構築など Services を要する初期化はここに書く。
		/// 遷移由来の引数（reader / ctx）は渡さない。初期 navigation data が要る初期化は OnBeforeLoad に書くこと。
		/// </summary>
		UniTask OnInitialize(CancellationToken ct) => UniTask.CompletedTask;

		// 6 hook は遷移ごとの ITransitionContext を受け取る。Effect と同じ ctx を共有するので、
		// Presenter から ctx.PublishStage / ctx.WaitForStage で Effect と細粒度連携できる。
		// reader/writer はフェーズ固有の bag（Pop の returnStore 等）で、ctx.Reader/Writer とは別物なので両方渡す。
		UniTask OnBeforeLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterLoad(IScreenViewInstance view, INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnBeforeShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnBeforeHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnSuspend(CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnResume(CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterUnload(INavigationDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;

		/// <summary>Navigator が生成直後にサービスバンドルを差し込む。既定は no-op。</summary>
		void AssignServices(ScreenServices services) { }
	}

	/// <summary>
	/// View を Input / Output で分離して保持する Presenter 基底。
	/// Presenter は <see cref="In"/> から購読・読み取り、<see cref="Out"/> へ呼び出し・書き込み。
	/// MockGenerator が生成する IXxxInput / IXxxOutput を TInput / TOutput に指定する想定。
	/// </summary>
	public abstract class ScreenPresenter<TInput, TOutput> : IScreenPresenter
		where TInput : class
		where TOutput : class
	{
		protected TInput  In  { get; private set; }
		protected TOutput Out { get; private set; }

		/// <summary>Navigator から注入される共通サービス。プロジェクト基底で型付きに細める想定。</summary>
		protected ScreenServices Services { get; private set; }

		void IScreenPresenter.AssignServices(ScreenServices services) => Services = services;

		UniTask IScreenPresenter.OnInitialize(CancellationToken ct) => OnInitialize(ct);

		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
			=> OnBeforeLoad(reader, ctx, ct);

		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance view, INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			In  = view.As<TInput>();
			Out = view.As<TOutput>();
			return OnAfterLoad(reader, ctx, ct);
		}

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
