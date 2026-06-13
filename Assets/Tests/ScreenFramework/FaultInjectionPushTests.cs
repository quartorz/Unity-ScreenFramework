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
	/// フォールトインジェクションテスト: Push の注入点のうち、<b>MBT 語彙外</b>のものだけ。
	/// ロードパイプラインの fault（handle.Load / OnInitialize / OnBeforeLoad / OnAfterLoad / Configure /
	/// factory throw / 偽 OCE / 二重フォールト）と commit ゾーンの吸収・回復可能性は、モデルベーステスト
	/// （<c>ModelBased/</c>）が直接カバーするため引退した（2026-06-13。docs/MODEL-BASED-TESTING.md の引退節）。
	/// ここに残すのは:
	/// - null 返却の <b>Identifier 契約違反</b>（handle / presenter / view が null）— MBT の語彙外
	/// - <b>PlayEnter</b> 演出の発火と OnSuspend → Resume の<b>表示状態</b> — MBT のモック View は
	///   IScreenAnimatedView 非実装・Suspend 表示状態を観測しないため語彙外
	/// commit ゾーンの例外は Debug.LogException されるので該当テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionPushTests : FaultInjectionTestBase
	{
		[Test]
		public async Task HandleLoadReturnsNullView_PushFails_Compensates_AndRecovers()
		{
			// handle が「成功」しつつ null view を返す契約違反。ロールバック可能ゾーンの
			// 失敗として伝播し、補償が走り、Navigator は壊れない。
			SetupNavigator();
			var handle = new NullViewHandle();
			var presenter = new TrackingPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsNotNull(caught, "null view は失敗として伝播する");
			Assert.IsNotInstanceOf<OperationCanceledException>(caught, "キャンセル扱いに化けない");
			Assert.IsTrue(handle.UnloadCalled, "契約違反の handle にも補償 Unload が呼ばれる");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "破棄経路でも OnAfterUnload が呼ばれる");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "契約違反の後も次の Push が成立する");
		}

		[Test]
		public async Task Push_PlayEnterThrows_PushCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at PlayEnter"));

			var id = new ControllableScreenId(new WrappingHandle(new FaultyAnimView(failEnter: true)));
			await ScreenNavigator.Page.Push(id);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(id, ScreenNavigator.Page.Current, "入場演出の失敗で画面が孤児にならない");
		}

		[Test]
		public async Task Push_CoveredScreenOnSuspendThrows_PushCompletes_AndResumeStillWorks()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover);
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at Suspend"));

			var presenterA = new FaultyPresenter("Suspend");
			var idA = new ControllableScreenId(new InstantHandle(), () => presenterA);
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "OnSuspend の失敗で Push が中断しない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);

			// Suspended フラグは hook の失敗後も立っており、Pop で通常どおり Resume される
			await ScreenNavigator.Page.Pop();
			CollectionAssert.Contains(presenterA.Events, "Resume");
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task CreateHandleReturnsNull_PushFails_CompensationStillRuns_AndRecovers()
		{
			// CreateHandle が null を返す契約違反(throw する factory は既存テストで担保済み)。
			// rollback ゾーンの失敗として伝播し、presenter 側には OnAfterUnload の補償チャンスが与えられる。
			SetupNavigator();
			var presenter = new TrackingPresenter();
			var id = new ControllableScreenId(Handle: null, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<NullReferenceException>(caught, "null handle は rollback ゾーンの失敗として伝播する");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "handle が null でも presenter 側の補償 hook は呼ばれる");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "失敗した Push は履歴に何も残さない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "契約違反の後も次の Push が成立する");
		}

		[Test]
		public async Task CreatePresenterReturnsNull_PushFails_AndRecovers()
		{
			// CreatePresenter が null を返す契約違反。何もロードされる前(AssignServices)に失敗し、
			// 副作用なしで伝播する。handle factory には到達しない。
			SetupNavigator();
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => null);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<NullReferenceException>(caught, "null presenter はロード開始前の失敗として伝播する");
			Assert.IsFalse(handle.UnloadCalled, "handle には触れていない(Load していないので補償も不要)");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "契約違反の後も次の Push が成立する");
		}
	}
}
