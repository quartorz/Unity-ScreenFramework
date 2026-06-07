using ScreenFramework;

namespace Tests.Support
{
	/// <summary>
	/// IScreenNavigator のモック実装。MockGenerator が partial を埋めて
	/// 各メソッドに対応する XxxFunc プロパティを生やしてくれる。
	/// </summary>
	[MockGenerator.GenerateMockFor(typeof(IScreenNavigator))]
	public partial class MockScreenNavigator { }
}
