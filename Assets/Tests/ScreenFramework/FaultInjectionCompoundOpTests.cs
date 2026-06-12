using System;
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
	/// フォールトインジェクションテスト: 複合操作(DismissAll / Reset / Change / Replace / PopTo / Close)の注入点。
	/// 「先ロード → 成功後に破棄」型(Reset / Change / Replace)はロード失敗時に既存スタックを丸ごと温存して
	/// ロールバックし伝播する。破棄フェーズ(完走必須ゾーン)の hook 失敗・中間 Close の teardown 失敗は
	/// ログに留めて操作を完走させる。伝播する例外は「先ロード」の失敗、復元ロード(PopTo / Close(top) の
	/// 最終段。dormant top で着地)、ロード前のユーザーコールバック(PopTo の predicate / Close の Configure)。
	/// 死んだ entry への Close は no-op。
	/// commit ゾーンの例外は Debug.LogException されるので各テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionCompoundOpTests : FaultInjectionTestBase
	{
		[Test]
		public async Task DismissAll_WithThrowingExitHooks_StillClearsEverything()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover); // A も生かしたまま DismissAll に巻き込む
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit"));
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterUnload"));

			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("AfterUnload")));
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("BeforeExit")));

			await ScreenNavigator.Page.DismissAll();

			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "退場 hook が落ちても全画面が畳まれる");
		}

		[Test]
		public async Task Reset_TopExitHookThrows_StillCollapsesToNewScreen()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterExit"));

			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("AfterExit")));

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Reset(idC);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "破壊フェーズの hook 失敗で Reset が中断しない");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task PopTo_MiddleScreenExitHookThrows_StillReachesTarget()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover);   // 中間 B を生かしたまま silent 破棄に巻き込む
			// B の BeforeExit が走るのは C に覆われる Push 時の 1 回だけ。そこで suspend 済みになるので、
			// PopTo での破棄では Exit hook を再走させず teardown のみ行い、二度目の throw は起きない。
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("BeforeExit")));
			await ScreenNavigator.Page.Push(new MarkerScreenId("C"));

			await ScreenNavigator.Page.PopTo(id => id is MarkerScreenId m && m.Label == "A");

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "中間画面の hook 失敗で PopTo が中断しない");
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task PopTo_PredicateThrows_Propagates_AndStackIsIntact()
		{
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);

			Exception caught = null;
			try
			{
				await ScreenNavigator.Page.PopTo(_ => throw new InvalidOperationException("fault injected at PopTo predicate"));
			}
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "predicate の失敗は伝播する");
			Assert.AreEqual(3, ScreenNavigator.Page.History.Count, "対象検索中の失敗なのでスタックは無傷");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			await ScreenNavigator.Page.PopTo(id => id is MarkerScreenId m && m.Label == "A");
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "フォールト後も通常の PopTo が成立する");
		}

		[Test]
		public async Task Replace_LoadFails_RollsBack_AndOldScreenSurvives()
		{
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			var handle = new FaultyLoadHandle();
			var id = new ControllableScreenId(handle, () => new TrackingPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Replace(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Replace の load 失敗は伝播する");
			Assert.IsTrue(handle.UnloadCalled, "失敗した load は補償 Unload される");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "失敗した Replace は既存スタックを壊さない");
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "旧画面が Current のまま生き残る");

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Replace(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "失敗後も Replace を再試行できる");
		}

		[Test]
		public async Task Change_LoadFails_WholeStackSurvives_AndChangeCanBeRetried()
		{
			// Change は「先ロード → 成功後に下スタック破棄」。ロード失敗時は下スタック含め全体が無傷で残る。
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);
			var handle = new FaultyLoadHandle();
			var idX = new ControllableScreenId(handle, () => new TrackingPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Change(idX); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Change の load 失敗は伝播する");
			Assert.IsTrue(handle.UnloadCalled, "失敗した load は補償 Unload される");
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "失敗した Change は下スタックも壊さない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "旧最上段が Current のまま生き残る");

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Change(idC);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "失敗後も Change を再試行できる");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Reset_LoadFails_ExistingStackSurvives()
		{
			// Reset も「先ロード → 成功後に全破壊」。ロード失敗で黒画面(スタック 0 枚)にならない。
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);
			var handle = new FaultyLoadHandle();
			var idX = new ControllableScreenId(handle, () => new TrackingPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Reset(idX); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Reset の load 失敗は伝播する");
			Assert.IsTrue(handle.UnloadCalled);
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "失敗した Reset は既存スタックを壊さない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Reset(idC);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "失敗後も Reset を再試行できる");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Change_BottomTeardownHookThrows_ChangeStillCompletes()
		{
			// Change の下スタック silent 破棄(完走必須ゾーン)中の hook 失敗は吸収され、単一画面化が完了する。
			SetupNavigator(ScreenCacheMode.KeepOnCover);   // 下の A を生かしたまま破棄に巻き込む
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterUnload"));

			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("AfterUnload")));
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Change(idC);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "下スタック破棄中の hook 失敗で Change が中断しない");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Close_TopExitHookThrows_CloseCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			var entry = await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("BeforeExit")));

			await entry.Close();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "退場 hook の失敗で Close が中断しない");
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
			Assert.IsFalse(entry.IsAlive, "失敗した hook を持つエントリも閉じ切られる");
		}

		[Test]
		public async Task Close_LastScreenExitHookThrows_StillClosesAndRecovers()
		{
			// Close は Pop と違い最後の 1 枚も閉じられる。その経路でも hook 失敗が畳み残しを生まない。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit"));

			var entry = await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("BeforeExit")));

			await entry.Close();

			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "最後の 1 枚でも hook 失敗で Close が中断しない");

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "空スタックからの再 Push が成立する");
		}

		[Test]
		public async Task CloseMiddle_UnloadThrows_CloseCompletes_AndStackStaysCoherent()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover);   // 中間 A を生きたまま(suspended)Close に巻き込む
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at handle\\.Unload"));

			var presenterA = new TrackingPresenter();
			var entryA = await ScreenNavigator.Page.Push(new ControllableScreenId(new FaultyUnloadHandle(), () => presenterA));
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);

			await entryA.Close();   // 最上段ではない = 中間 Close 経路

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "teardown の失敗で中間 Close が中断しない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "最上段は据え置かれる");
			Assert.IsFalse(entryA.IsAlive, "Unload が失敗した entry も閉じ切られる");
			Assert.IsTrue(presenterA.OnAfterUnloadCalled, "Unload の失敗後も OnAfterUnload まで進む");

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task Replace_OldTopExitHookThrows_ReplaceCompletes()
		{
			// Replace の旧最上段退場は commit ゾーン(ロード成功後)。退場 hook の失敗で差し替えが中断しない。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit"));

			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("BeforeExit")));

			var idB = new MarkerScreenId("B");
			var entry = await ScreenNavigator.Page.Replace(idB);

			Assert.IsNotNull(entry, "退場 hook が落ちても新画面のエントリは返る");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "旧最上段の退場 hook 失敗で Replace が中断しない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task Change_OldTopExitHookThrows_ChangeCompletes()
		{
			// Change の cross-fade 退場(現最上段)も commit ゾーン。hook の失敗で単一画面化が中断しない。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterExit"));

			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("AfterExit")));

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Change(idC);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "差し替え退場の hook 失敗で Change が中断しない");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task Replace_OnEmptyStack_LoadFails_NothingChanges()
		{
			// 空スタックの Replace は Push 委譲経路。その rollback ゾーンの失敗も伝播 + 完全クリーンアップ。
			SetupNavigator();
			var handle = new FaultyLoadHandle();
			var id = new ControllableScreenId(handle, () => new TrackingPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Replace(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Push 委譲経路でも load 失敗は伝播する");
			Assert.IsTrue(handle.UnloadCalled, "失敗した load は補償 Unload される");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "空スタックのまま何も残らない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task PopTo_RestoreLoadFails_Propagates_AndNavigatorRecovers()
		{
			// PopTo の最終段(PopCore)の復元ロード失敗。中間破棄と top の退場は完了済みで巻き戻さず、
			// Pop と同じ「dormant top + 伝播」契約に着地する。
			SetupNavigator();
			var creations = 0;
			var idA = new ControllableScreenId(new InstantHandle(), () =>
				++creations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("C"));

			Exception caught = null;
			try { await ScreenNavigator.Page.PopTo(id => ReferenceEquals(id, idA)); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "復元ロードの失敗は PopTo でも伝播する");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "中間の破棄と top の退場は完了している(巻き戻さない)");
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "履歴上の Current は A のまま(dormant)");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idD = new MarkerScreenId("D");
			await ScreenNavigator.Page.Push(idD);
			Assert.AreSame(idD, ScreenNavigator.Page.Current, "dormant top の上にも次の Push が成立する");
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task Close_TopRestoreLoadFails_Propagates_AndNavigatorRecovers()
		{
			// Close(top) は Pop 相当の復元ロードを持つ。失敗時は B の退場を巻き戻さず伝播し、dormant top で着地する。
			SetupNavigator();
			var creations = 0;
			var idA = new ControllableScreenId(new InstantHandle(), () =>
				++creations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			await ScreenNavigator.Page.Push(idA);
			var entryB = await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			Exception caught = null;
			try { await entryB.Close(); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Close(top) の復元ロード失敗も伝播する");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "B の退場は完了している(巻き戻さない)");
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "履歴上の Current は A のまま(dormant)");
			Assert.IsFalse(entryB.IsAlive, "閉じた entry は IsAlive=false");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "dormant top の上にも次の Push が成立する");
		}

		[Test]
		public async Task Close_TopConfigureThrows_Propagates_AndStackIsIntact()
		{
			SetupNavigator();
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			var presenterB = new RecordingPresenter();
			var entryB = await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => presenterB));

			Exception caught = null;
			try
			{
				await entryB.Close(new PopOptions
				{
					Configure = _ => throw new InvalidOperationException("fault injected at Close Configure"),
				});
			}
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Close(top) の Configure の失敗は伝播する");
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "退場前の失敗なのでスタックは無傷");
			Assert.IsTrue(entryB.IsAlive, "閉じられていない");
			CollectionAssert.DoesNotContain(presenterB.Events, "BeforeExit", "Exit hook には到達していない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			await entryB.Close();
			Assert.IsFalse(entryB.IsAlive, "フォールト後も通常の Close が成立する");
		}

		[Test]
		public async Task Close_MiddleConfigureThrows_Propagates_AndStackIsIntact()
		{
			// 中間 Close も top と同じく Configure を ctx に seed する(以前は無言で捨てられていた)。
			// その失敗は退場前なので伝播し、スタックは無傷。
			SetupNavigator(ScreenCacheMode.KeepOnCover);   // 中間 A を生かしたまま Close 対象にする
			var presenterA = new TrackingPresenter();
			var entryA = await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => presenterA));
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);

			Exception caught = null;
			try
			{
				await entryA.Close(new PopOptions
				{
					Configure = _ => throw new InvalidOperationException("fault injected at Close Configure (middle)"),
				});
			}
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "中間 Close でも Configure は呼ばれ、失敗は伝播する");
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "退場前の失敗なのでスタックは無傷");
			Assert.IsTrue(entryA.IsAlive, "閉じられていない");
			Assert.IsFalse(presenterA.OnAfterUnloadCalled, "teardown には到達していない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "最上段は据え置かれる");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			await entryA.Close();
			Assert.IsFalse(entryA.IsAlive, "フォールト後も通常の中間 Close が成立する");
		}

		[Test]
		public async Task EntryClose_AfterSweptByDismissAll_IsNoOp()
		{
			SetupNavigator();
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			var entry = await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			await ScreenNavigator.Page.DismissAll();
			Assert.IsFalse(entry.IsAlive, "DismissAll で破棄された entry は IsAlive=false");

			await entry.Close();   // 「既に閉じている / 未 Push なら何もしない」契約。例外にならない

			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "死んだ entry への Close は状態を変えない");

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "no-op の後も次の Push が成立する");
		}
	}
}
