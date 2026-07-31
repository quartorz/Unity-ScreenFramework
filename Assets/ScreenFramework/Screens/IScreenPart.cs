using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// 画面を構成する部品（Feature / Binder）の共通 IF。
	/// <see cref="ComposedScreenPresenter{TInput,TOutput}"/> の Compose で生成され、
	/// 各ライフサイクル hook が Presenter から fan-out される。
	///
	/// Part は Compose（= OnAfterLoad 時点）で生成されるため、OnInitialize / OnBeforeLoad は持たない。
	/// すべて default 実装（no-op）なので、必要な hook だけ実装すればよい。
	/// 引数は Presenter の同名 hook と同一（reader/writer/ctx/ct）を素通しで受け取る。
	/// </summary>
	public interface IScreenPart
	{
		UniTask OnAfterLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnBeforeShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterShow(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnBeforeHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnSuspend(CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnResume(CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterUnload(INavigationDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;
	}
}
