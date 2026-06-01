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

	public abstract class ScreenPresenter<TView> : IScreenPresenter where TView : class
	{
		protected TView View { get; private set; }

		UniTask IScreenPresenter.OnBeforeLoad(IScreenDataReader reader, CancellationToken ct)
			=> OnBeforeLoad(reader, ct);

		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance view, IScreenDataReader reader, CancellationToken ct)
		{
			View = view.As<TView>();
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
