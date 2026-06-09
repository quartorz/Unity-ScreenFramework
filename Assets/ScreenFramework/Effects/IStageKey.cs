namespace ScreenFramework
{
	/// <summary>
	/// stage signal のキー用マーカーインターフェース。
	/// 利用者は <c>public sealed class DataReadyStage : IStageKey {}</c> のような空マーカー型を 1 行宣言し、
	/// <see cref="ITransitionContext.PublishStage{TStage}"/> と <see cref="ITransitionContext.WaitForStage{TStage}"/>
	/// のジェネリック引数に渡す。型自体がキーなので文字列や SO は使わずコンパイル完全安全。
	/// </summary>
	public interface IStageKey { }
}
