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

		/// <summary>
		/// <para>画面ロード前でViewはまだ存在しない</para>
		/// <para>メソッド内で例外を投げたり、どこかで<see cref="InterruptPriority.Preempt">Preempt</see>な遷移を行うと遷移がキャンセルされる</para>
		/// </summary>
		UniTask OnBeforeLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		/// <summary>
		/// <para>画面ロード後、Viewを表示する前</para>
		/// <para>メソッド内で例外を投げたり、どこかで<see cref="InterruptPriority.Preempt">Preempt</see>な遷移を行うと遷移がキャンセルされる</para>
		/// </summary>
		UniTask OnAfterLoad(IScreenViewInstance view, INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		/// <summary>
		/// <para>画面ロード後、Viewを表示する前</para>
		/// <para>メソッド内で例外を投げたりしても遷移を止めることはできない</para>
		/// </summary>
		UniTask OnBeforeShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		/// <summary>
		/// <para>Viewを表示したあと</para>
		/// <para>メソッド内で例外を投げたりしても遷移を止めることはできない</para>
		/// </summary>
		UniTask OnAfterShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		/// <summary>
		/// 上に別の画面が重なって、キャッシュされたり破棄されたりする前
		/// </summary>
		UniTask OnBeforeHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		/// <summary>
		/// 上に別の画面が重なって、退場アニメーションが再生されて、Viewがキャッシュされたり破棄されたりしたあと
		/// </summary>
		UniTask OnAfterHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		/// <summary>
		/// <see cref="ScreenCacheMode"/>が<see cref="ScreenCacheMode.KeepOnCover">KeepOnCover</see>の画面がキャッシュされて、<see cref="OnAfterHide"/>が呼ばれたあと
		/// </summary>
		UniTask OnSuspend(CancellationToken ct) => UniTask.CompletedTask;
		/// <summary>
		/// <see cref="ScreenCacheMode"/>が<see cref="ScreenCacheMode.KeepOnCover">KeepOnCover</see>の画面が再表示されるとき
		/// </summary>
		UniTask OnResume(CancellationToken ct) => UniTask.CompletedTask;
		/// <summary>
		/// <see cref="ScreenCacheMode"/>が<see cref="ScreenCacheMode.DestroyOnCover">DestroyOnCover</see>のViewが破棄されたあと
		/// </summary>
		UniTask OnAfterUnload(INavigationDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;

		/// <summary>Navigator が生成直後にサービスバンドルを差し込む。既定は no-op。</summary>
		void AssignServices(ScreenServices services) { }
	}

	/// <summary>
	/// View を Input / Output で分離して保持する Presenter 基底（小〜中画面用）。
	/// Presenter は <see cref="In"/> から購読・読み取り、<see cref="Out"/> へ呼び出し・書き込み。
	/// MockGenerator が生成する IXxxInput / IXxxOutput を TInput / TOutput に指定する想定。
	/// </summary>
	public abstract class ScreenPresenter<TInput, TOutput> : ScreenPresenterBase<TInput, TOutput>, IScreenPresenter
		where TInput : class
		where TOutput : class
	{
		protected TInput  In  { get; private set; }
		protected TOutput Out { get; private set; }

		// View を取り込んで In/Out を公開するため IScreenPresenter.OnAfterLoad を再実装する。
		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance view, INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			In  = view.As<TInput>();
			Out = view.As<TOutput>();
			return OnAfterLoad(reader, ctx, ct);
		}
	}
}
