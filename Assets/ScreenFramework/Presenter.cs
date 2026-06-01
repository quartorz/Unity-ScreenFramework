using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	public interface IScreenPresenter
	{
		UniTask OnBeforeLoad(IScreenDataReader reader, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterLoad(IScreenViewInstance view, IScreenDataReader reader, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnBeforeEnter(IScreenDataReader reader, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterEnter(IScreenDataReader reader, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnBeforeExit(IScreenDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterExit(IScreenDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnSuspend(CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnResume(CancellationToken ct) => UniTask.CompletedTask;
		UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;
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

		UniTask IScreenPresenter.OnBeforeLoad(IScreenDataReader reader, CancellationToken ct)
			=> OnBeforeLoad(reader, ct);

		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance view, IScreenDataReader reader, CancellationToken ct)
		{
			In  = view.As<TInput>();
			Out = view.As<TOutput>();
			return OnAfterLoad(reader, ct);
		}

		UniTask IScreenPresenter.OnBeforeEnter(IScreenDataReader reader, CancellationToken ct) => OnBeforeEnter(reader, ct);
		UniTask IScreenPresenter.OnAfterEnter(IScreenDataReader reader, CancellationToken ct) => OnAfterEnter(reader, ct);
		UniTask IScreenPresenter.OnBeforeExit(IScreenDataWriter writer, CancellationToken ct) => OnBeforeExit(writer, ct);
		UniTask IScreenPresenter.OnAfterExit(IScreenDataWriter writer, CancellationToken ct) => OnAfterExit(writer, ct);
		UniTask IScreenPresenter.OnSuspend(CancellationToken ct) => OnSuspend(ct);
		UniTask IScreenPresenter.OnResume(CancellationToken ct) => OnResume(ct);
		UniTask IScreenPresenter.OnAfterUnload(IScreenDataWriter writer, CancellationToken ct) => OnAfterUnload(writer, ct);

		protected virtual UniTask OnBeforeLoad(IScreenDataReader reader, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnBeforeEnter(IScreenDataReader reader, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnAfterEnter(IScreenDataReader reader, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnBeforeExit(IScreenDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnAfterExit(IScreenDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnSuspend(CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnResume(CancellationToken ct) => UniTask.CompletedTask;
		protected virtual UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;
	}
}
