using ScreenFramework;

namespace Sample
{
	/// <summary>
	/// Sample プロジェクト共通の Presenter 基底。
	/// <see cref="ScreenPresenter{TInput,TOutput}.Services"/> を <see cref="SampleServices"/> 型で参照できるよう包む。
	/// Navigator が生成直後に自動注入する。
	/// </summary>
	public abstract class SamplePresenter<TInput, TOutput, TView> : ScreenPresenter<TInput, TOutput>
		where TInput : class
		where TOutput : class
		where TView : TInput, TOutput
	{
		protected new SampleServices Services => (SampleServices)base.Services;
	}
	/// <summary>
	/// Sample プロジェクト共通の Presenter 基底。
	/// <see cref="ScreenPresenter{TInput,TOutput}.Services"/> を <see cref="SampleServices"/> 型で参照できるよう包む。
	/// Navigator が生成直後に自動注入する。
	/// </summary>
	public abstract class SamplePresenter<TInput, TOutput> : ScreenPresenter<TInput, TOutput>
		where TInput : class
		where TOutput : class
	{
		protected new SampleServices Services => (SampleServices)base.Services;
	}
}
