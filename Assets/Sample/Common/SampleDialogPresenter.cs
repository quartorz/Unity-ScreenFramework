using ScreenFramework;

namespace Sample
{
	/// <summary>
	/// Sample プロジェクト共通の Dialog Presenter 基底。
	/// <see cref="SamplePresenter{TInput,TOutput,TView}"/> と同様、
	/// <see cref="Registry"/> 経由で <see cref="SampleRegistry"/> を参照できる。
	/// </summary>
	public abstract class SampleDialogPresenter<TInput, TOutput, TResult>
		: DialogPresenter<TInput, TOutput, TResult>
		where TInput : class
		where TOutput : class
		where TResult : INavigationData
	{
		protected SampleRegistry Registry => (SampleRegistry)Services;
	}
}
