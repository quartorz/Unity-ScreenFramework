using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// ScreenNavigator.Shutdown() と再 Initialize の挙動を検証する。
	/// 旧実装は再 Initialize で旧 navigator の画面群が孤児化し、pending PushAndAwait の awaiter が永久未解決だった。
	/// </summary>
	public sealed class ShutdownTests
	{
		IScreenContainer _page, _dialog, _sys;

		[SetUp]
		public void SetUp()
		{
			_page = NewContainer("PageRoot");
			_dialog = NewContainer("DialogRoot");
			_sys = NewContainer("SysRoot");
			InitializeNavigator();
		}

		[TearDown]
		public void TearDown()
		{
			ScreenNavigator.Shutdown();
			DestroyContainer(_page);
			DestroyContainer(_dialog);
			DestroyContainer(_sys);
		}

		void InitializeNavigator()
		{
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_page),
				Dialog = NewLayer(_dialog),
				SystemDialog = NewLayer(_sys),
			});
		}

		[Test]
		public async Task Shutdown_ClearsStaticRefs()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);

			ScreenNavigator.Shutdown();

			Assert.IsNull(ScreenNavigator.Page);
			Assert.IsNull(ScreenNavigator.Dialog);
			Assert.IsNull(ScreenNavigator.SystemDialog);
		}

		[Test]
		public async Task Shutdown_CancelsPendingPushAndAwait()
		{
			// 結果を SetResult しないダイアログ。awaiter は開いたまま（Pop されるまで解決しない）。
			var task = ScreenNavigator.Page.PushAndAwait(new EchoDialogId(null));
			await UniTask.Yield();
			await UniTask.Yield();

			ScreenNavigator.Shutdown();

			try
			{
				await task;
				Assert.Fail("Shutdown should cancel the pending PushAndAwait awaiter");
			}
			catch (OperationCanceledException) { /* 期待 */ }
		}

		[Test]
		public async Task Reinitialize_ShutsDownPrevious()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			var oldPage = ScreenNavigator.Page;
			Assert.AreEqual(2, oldPage.History.Count);

			// 再 Initialize → 旧 navigator を自動 Shutdown してから差し替え
			InitializeNavigator();

			Assert.AreNotSame(oldPage, ScreenNavigator.Page, "新しい navigator に差し替わる");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "新 navigator は空");
			Assert.AreEqual(0, oldPage.History.Count, "旧 navigator は Shutdown されて空（孤児化しない）");
		}
	}
}
