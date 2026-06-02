using ScreenFramework;

namespace Sample
{
	/// <summary>
	/// Sample プロジェクト共通の Dialog Presenter 基底。
	/// <see cref="SamplePresenter{TInput,TOutput}"/> と同様、Services を <see cref="SampleServices"/> 型で参照できる。
	/// </summary>
	public abstract class SampleDialogPresenter<TInput, TOutput, TResult>
		: DialogPresenter<TInput, TOutput, TResult>
		where TInput : class
		where TOutput : class
		where TResult : IScreenData
	{
		protected new SampleServices Services => (SampleServices)base.Services;
	}
}
