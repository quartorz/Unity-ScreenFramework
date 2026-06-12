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
	/// フォールトインジェクションテスト: Pop の注入点。Pop は全段が完走必須(commit)ゾーンなので、
	/// 退場 hook(OnBeforeExit / OnAfterExit / OnResume)・teardown(handle.Unload / OnAfterUnload)・
	/// 退場演出(PlayExit)の失敗はログに留めて Pop を完走させる契約。例外は
	/// 復元ロード(非ガード = 伝播)とロード前のユーザーコールバック(Configure)に限られる。
	/// commit ゾーンの例外は Debug.LogException されるので各テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionPopTests : FaultInjectionTestBase
	{
		[Test]
		public async Task Pop_RestoreLoadFails_PropagatesButNavigatorRemainsUsable()
		{
			SetupNavigator(); // DestroyOnCover: 覆われた A は Pop 時に再ロードされる
			var creations = 0;
			// 1 回目(Push 時)は成功し、2 回目(Pop の復元時)だけ失敗する presenter factory
			var idA = new ControllableScreenId(new InstantHandle(), () =>
				++creations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			Exception caught = null;
			try { await ScreenNavigator.Page.Pop(); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "復元ロードの失敗は呼び出し側へ伝播する");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "B の退場は完了している(巻き戻さない)");
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "履歴上の Current は A のまま(dormant)");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			// dormant な最上段の上にも通常の Push が成立する(黒画面からの復帰経路がある)
			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task Pop_OnBeforeExitThrows_PopCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("BeforeExit")));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "退場 hook の失敗で Pop が中断しない");
		}

		[Test]
		public async Task Pop_HandleUnloadThrows_PopCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at handle\\.Unload"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new FaultyUnloadHandle()));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "Unload の失敗で bookkeeping が止まらない");
		}

		[Test]
		public async Task Pop_OnAfterUnloadThrows_PopCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterUnload"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("AfterUnload")));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Pop_OnResumeThrows_PopCompletes()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover);
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at Resume"));

			var idA = new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("Resume"));
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "OnResume の失敗で Pop が中断しない");
		}

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
		public async Task Pop_RevealedScreenEnterHookThrows_PopCompletes()
		{
			// 復帰側(below)の Enter hook も完走必須ゾーン。失敗はログに留まり Pop は中断しない。
			// FaultyPresenter("BeforeEnter") は自分の Push 時(commit ゾーン)にも一度 throw するので 2 回 Expect する。
			SetupNavigator(ScreenCacheMode.KeepOnCover);
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeEnter"));
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeEnter"));

			var presenterA = new FaultyPresenter("BeforeEnter");
			var idA = new ControllableScreenId(new InstantHandle(), () => presenterA);
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "復帰側の Enter hook の失敗で Pop が中断しない");
			CollectionAssert.Contains(presenterA.Events, "Resume", "Enter hook より前の Resume は通常どおり走っている");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
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

		[Test]
		public async Task Pop_ConfigureThrows_Propagates_AndStackIsIntact()
		{
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			var presenterB = new RecordingPresenter();
			var idB = new ControllableScreenId(new InstantHandle(), () => presenterB);
			await ScreenNavigator.Page.Push(idB);

			Exception caught = null;
			try
			{
				await ScreenNavigator.Page.Pop(new PopOptions
				{
					Configure = _ => throw new InvalidOperationException("fault injected at Pop Configure"),
				});
			}
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Pop Configure の失敗は伝播する");
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "退場前の失敗なのでスタックは無傷");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
			CollectionAssert.DoesNotContain(presenterB.Events, "BeforeExit", "Exit hook には到達していない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			await ScreenNavigator.Page.Pop();
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "フォールト後も通常の Pop が成立する");
		}
	}
}
