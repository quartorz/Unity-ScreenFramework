using ScreenFramework;

namespace Sample
{
	/// <summary>
	/// Sample プロジェクト共通の Presenter 基底。
	/// <see cref="ScreenPresenter{TInput,TOutput}.Services"/> を <see cref="SampleRegistry"/> 型で
	/// <see cref="Registry"/> プロパティから参照できるよう包む。
	/// Navigator が生成直後に自動注入する。
	/// </summary>
	public abstract class SamplePresenter<TInput, TOutput> : ScreenPresenter<TInput, TOutput>
		where TInput : class
		where TOutput : class
	{
		protected SampleRegistry Registry => (SampleRegistry)Services;
	}
}
