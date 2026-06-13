using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// フォールトインジェクションテスト: Pop の注入点のうち、<b>演出発火</b>のものだけ。
	/// commit ゾーンの hook 吸収（OnBeforeExit / OnAfterUnload / OnResume / 復帰側 Enter hook）・
	/// 復元ロード失敗の dormant top 着地・Configure 例外の伝播は、モデルベーステスト（<c>ModelBased/</c>）が
	/// 直接カバーするため引退した（2026-06-13。docs/MODEL-BASED-TESTING.md の引退節）。
	/// PlayExit（退場演出）と復帰側 PlayEnter（復帰演出）の発火は、MBT のモック View が
	/// IScreenAnimatedView 非実装で注入できないためここに残す。
	/// commit ゾーンの例外は Debug.LogException されるので各テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionPopTests : FaultInjectionTestBase
	{
		[Test]
		public async Task Pop_PlayExitThrows_PopCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at PlayExit"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new WrappingHandle(new FaultyAnimView(failExit: true))));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "退場演出の失敗で Pop が中断しない");
		}

		[Test]
		public async Task Pop_RevealedScreenPlayEnterThrows_PopCompletes()
		{
			// DestroyOnCover: A は Pop で復元され、復帰演出(PlayEnter)が走る。演出の失敗で Pop は中断しない。
			// 初回 Push 時の PlayEnter でも一度 throw するので 2 回 Expect する。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at PlayEnter"));
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at PlayEnter"));

			var idA = new ControllableScreenId(new WrappingHandle(new FaultyAnimView(failEnter: true)));
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "復帰演出の失敗で Pop が中断しない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}
	}
}
