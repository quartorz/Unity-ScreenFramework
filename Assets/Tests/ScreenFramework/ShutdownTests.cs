using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// ScreenNavigator.Shutdown()（非同期・DismissAll 演出あり）と再 Initialize（既初期化なら例外）の挙動を検証する。
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
			// 静的参照は同期的に外れるので、演出完了は待たずに後始末してよい（mock view は即時完了）。
			ScreenNavigator.Shutdown().Forget();
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

			await ScreenNavigator.Shutdown();

			Assert.IsNull(ScreenNavigator.Page);
			Assert.IsNull(ScreenNavigator.Dialog);
			Assert.IsNull(ScreenNavigator.SystemDialog);
		}

		[Test]
		public async Task Shutdown_CancelsPendingPushAndAwait()
		{
			// 結果を SetResult しないダイアログ。awaiter は開いたまま（Pop / Dismiss されるまで解決しない）。
			var task = ScreenNavigator.Page.PushAndAwait(new EchoDialogId(null));
			await UniTask.Yield();
			await UniTask.Yield();

			await ScreenNavigator.Shutdown();

			try
			{
				await task;
				Assert.Fail("Shutdown の DismissAll で pending awaiter がキャンセルされるべき");
			}
			catch (OperationCanceledException) { /* 期待 */ }
		}

		[Test]
		public void Initialize_WhenAlreadyInitialized_Throws()
		{
			// SetUp で初期化済み。Shutdown せずに再初期化すると例外。
			Assert.Throws<InvalidOperationException>(InitializeNavigator);
		}

		[Test]
		public async Task Shutdown_ThenInitialize_Works()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));

			await ScreenNavigator.Shutdown();
			Assert.IsNull(ScreenNavigator.Page);

			InitializeNavigator(); // Shutdown 済みなので例外にならない
			Assert.IsNotNull(ScreenNavigator.Page);

			await ScreenNavigator.Page.Push(new MarkerScreenId("X"));
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
		}
	}
}
