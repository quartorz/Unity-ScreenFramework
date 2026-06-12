using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// フォールトインジェクションテスト: PushAndAwait の決着保証。開いたダイアログが「正常 Pop / Close」で
	/// 閉じれば結果が届き(退場 hook が落ちても結果配送は壊れない)、それ以外の閉じ方
	/// (ロード失敗 / 外部キャンセル / preempt / Replace 上書き / DismissAll・PopTo の silent 破棄)では
	/// 結果待ちがハングせず OperationCanceledException で決着する、の両側を検証する。
	/// commit ゾーンの例外は Debug.LogException されるので各テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionPushAndAwaitTests : FaultInjectionTestBase
	{
		[Test]
		public async Task PushAndAwait_LoadFaulted_PropagatesInsteadOfHanging()
		{
			SetupNavigator();

			Exception caught = null;
			try { await ScreenNavigator.Page.PushAndAwait(new FaultyLoadDialogId()); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "ロード失敗は結果待ちのハングではなく例外で返る");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task PushAndAwait_CancelDuringLoad_AwaiterGetsOce_InsteadOfHanging()
		{
			SetupNavigator();
			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var id = new ControllableDialogId(new ControllableHandle(source));
			using var cts = new CancellationTokenSource();

			var awaitTask = ScreenNavigator.Page.PushAndAwait(id, ct: cts.Token);
			cts.Cancel();

			Exception caught = null;
			try { await awaitTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "結果待ちのハングではなく OCE で決着する");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		// 「ct は Push フェーズにのみ作用し結果待ちはキャンセルできない」契約は
		// PushAndAwaitTests.ExternalCt_DoesNotCancelWaitPhase_ByDesign が
		// キャンセル後も未解決のままであることまで含めて検証している。

		[Test]
		public async Task PushAndAwait_PreemptedDuringLoad_AwaiterGetsOce()
		{
			SetupNavigator();
			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var idDialog = new ControllableDialogId(new ControllableHandle(source));
			var idB = new MarkerScreenId("B");

			var awaitTask = ScreenNavigator.Page.PushAndAwait(idDialog);
			var pushB = ScreenNavigator.Page.Push(idB);

			Exception caught = null;
			try { await awaitTask; }
			catch (Exception e) { caught = e; }
			await pushB;

			Assert.IsInstanceOf<OperationCanceledException>(caught, "preempt rollback で awaiter は OCE で決着する(ハングしない)");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task PushAndAwait_ReplacedWhileOpen_AwaiterGetsOce()
		{
			// 「正常 Pop」以外の閉じ方(Replace 上書き)では結果は届かず OCE で決着する契約。
			SetupNavigator();
			await ScreenNavigator.Page.Push(new MarkerScreenId("Base"));
			var resultTask = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("never"));

			var idR = new MarkerScreenId("R");
			await ScreenNavigator.Page.Replace(idR);

			Exception caught = null;
			try { await resultTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "Replace で上書きされた dialog の awaiter は OCE");
			Assert.AreSame(idR, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task PushAndAwait_SweptByDismissAll_AwaiterGetsOce()
		{
			SetupNavigator();
			await ScreenNavigator.Page.Push(new MarkerScreenId("Base"));
			var resultTask = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("never"));

			await ScreenNavigator.Page.DismissAll();

			Exception caught = null;
			try { await resultTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "DismissAll で破棄された dialog の awaiter は OCE で決着する");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task PushAndAwait_SweptByPopTo_AwaiterGetsOce()
		{
			// KeepOnCover で生きたまま中間に埋まった dialog を PopTo が silent 破棄する経路。
			SetupNavigator(ScreenCacheMode.KeepOnCover);
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			var resultTask = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("never"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("C"));

			await ScreenNavigator.Page.PopTo(id => id is MarkerScreenId m && m.Label == "A");

			Exception caught = null;
			try { await resultTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "PopTo に巻き込まれた dialog の awaiter は OCE で決着する");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task PushAndAwait_ExitHookThrows_ResultIsStillDelivered()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterExit \\(dialog\\)"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("Base"));

			var resultTask = ScreenNavigator.Page.PushAndAwait(new FaultyExitEchoDialogId());
			await ScreenNavigator.Page.Pop();   // 正常 Pop。結果書き込み後の退場 hook が落ちる

			var result = await resultTask;
			Assert.IsNotNull(result, "退場 hook の失敗で結果配送が壊れない");
			Assert.AreEqual("delivered", result.Text);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task PushAndAwait_ClosedViaEntry_BeforeExitThrows_LastChanceWriteIsDelivered()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit \\(last-chance dialog\\)"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("Base"));

			var resultTask = ScreenNavigator.Page.PushAndAwait(new LastChanceEchoDialogId());
			var entry = ScreenNavigator.Page.FindEntry<LastChanceEchoPresenter>();
			Assert.IsNotNull(entry, "開いたダイアログのエントリが見つかる前提");

			await entry.Close();   // Close は「参照で閉じる Pop」= 正常クローズ扱い

			var result = await resultTask;
			Assert.IsNotNull(result, "退場 hook の失敗で結果配送が壊れない");
			Assert.AreEqual("last-chance", result.Text, "OnAfterUnload の書き込みが結果配送に間に合う");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
		}
	}
}
