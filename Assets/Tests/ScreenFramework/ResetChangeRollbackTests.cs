using System;
using System.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// Reset / Change が「先に新画面をロード（ロールバック可能）→ 成功してから既存スタックを破壊」する
	/// 並べ替えの回帰テスト。新画面のロードが失敗したとき、既存スタックを一切壊さずに復帰できること
	/// （旧実装は破壊が先で、Reset は「0 枚・Current=null の黒画面」、Change は下スタック消失になっていた）。
	/// </summary>
	public sealed class ResetChangeRollbackTests
	{
		IScreenContainer _pageContainer;

		[SetUp]
		public void SetUp()
		{
			_pageContainer = NewContainer("PageRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer),
				Dialog = NewLayer(NewContainer("DialogRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			});
		}

		[TearDown]
		public void TearDown() => DestroyContainer(_pageContainer);

		[Test]
		public async Task Reset_LoadFailure_KeepsExistingStackIntact()
		{
			var idA = new MarkerScreenId("A");
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);

			// ロードに失敗する画面で Reset
			var failingId = new ControllableScreenId(new InstantHandle(), () => new ThrowingOnBeforeLoadPresenter());
			try
			{
				await ScreenNavigator.Page.Reset(failingId);
				Assert.Fail("Reset should propagate the load failure");
			}
			catch (InvalidOperationException) { /* 期待 */ }

			// 破壊が起きていない＝既存スタックがそのまま。黒画面復帰不能にならないこと。
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "ロード失敗で既存スタックが消えてはいけない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "Current は元の最上段のまま");
		}

		[Test]
		public async Task Change_LoadFailure_KeepsBelowStackIntact()
		{
			var idA = new MarkerScreenId("A");
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);

			var failingId = new ControllableScreenId(new InstantHandle(), () => new ThrowingOnBeforeLoadPresenter());
			try
			{
				await ScreenNavigator.Page.Change(failingId);
				Assert.Fail("Change should propagate the load failure");
			}
			catch (InvalidOperationException) { /* 期待 */ }

			// 下スタック (A) も現在画面 (B) もそのまま。
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "ロード失敗で下スタックを失ってはいけない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "Current は元の最上段のまま");
		}

		[Test]
		public async Task Reset_Success_CollapsesToSingleScreen()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Reset(idC);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Change_Success_CollapsesToSingleScreen()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Change(idC);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}
	}
}
