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
	/// フォールトインジェクションテスト: Push の注入点。ロード境界(handle.Load の同期 throw /
	/// faulted UniTask / null view 返却)、rollback ゾーンの hook(OnInitialize / OnBeforeLoad /
	/// OnAfterLoad)、factory 境界(CreateHandle / CreatePresenter)、ユーザーコールバック(Configure)、
	/// 偽 OCE、二重フォールト(補償自体の失敗)、覆われた画面の OnSuspend を扱う。
	/// 「rollback ゾーンの失敗は伝播 + 完全クリーンアップ」「commit ゾーンの失敗はログに留めて完走」
	/// 「どのフォールト後も Navigator は次の操作を受け付けられる(復帰可能)」の 3 契約を検証する。
	/// commit ゾーンの例外は Debug.LogException されるので各テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionPushTests : FaultInjectionTestBase
	{
		[Test]
		public async Task HandleLoadFaulted_PushPropagates_CleansUp_AndNavigatorRecovers()
		{
			SetupNavigator();
			var handle = new FaultyLoadHandle();
			var presenter = new TrackingPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "rollback ゾーンの load 失敗は伝播する");
			Assert.IsTrue(handle.UnloadCalled, "失敗した load も handle.Unload で補償される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "load 失敗時も OnAfterUnload を呼ぶ契約");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "失敗した Push は追跡されない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning, "失敗後に遷移中フラグが残らない");

			// フォールト後も次の操作が成立する(復帰可能)
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task HandleLoadThrowsSynchronously_PushPropagates_AndCleansUp()
		{
			// faulted UniTask とは別経路(loadTask 起動前の同期 throw)。catch 側の
			// 「未起動タスクの決着待ちを skip して cleanup する」分岐を踏む。
			SetupNavigator();
			var handle = new FaultyLoadHandle(throwSynchronously: true);
			var presenter = new TrackingPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught);
			Assert.IsTrue(handle.UnloadCalled, "同期 throw でも handle.Unload で補償される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled);
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task HandleLoadAndOnBeforeLoadBothFail_PropagatesNonOce_AndUnloads()
		{
			// 並列起動される handle.Load と presenter.OnBeforeLoad が同時に失敗しても、
			// 互いの決着を待ってから cleanup し、元の例外型のまま(OCE に化けず)伝播する。
			SetupNavigator();
			var handle = new FaultyLoadHandle();
			var id = new ControllableScreenId(handle, () => new ThrowingOnBeforeLoadPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsNotNull(caught, "二重失敗でも例外は伝播する");
			Assert.IsNotInstanceOf<OperationCanceledException>(caught, "二重失敗でも OCE に詰め替えられない");
			Assert.IsTrue(handle.UnloadCalled, "二重失敗でも handle.Unload で補償される");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task OnInitializeThrows_PushPropagates_AndHandleIsNeverTouched()
		{
			SetupNavigator();
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => new FaultyPresenter("Initialize"));

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "OnInitialize の例外は伝播する");
			Assert.IsFalse(handle.UnloadCalled, "handle は OnInitialize より後に作られるので補償 Unload も呼ばれない");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task Push_OnBeforeLoadThrowsAlone_Propagates_AndLoadedViewIsCompensated()
		{
			// handle.Load は成功する(並列で view がロードされる)のに OnBeforeLoad だけが失敗するケース。
			// ロード済みの view が漏れずに補償 Unload されることを見る。
			SetupNavigator();
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => new ThrowingOnBeforeLoadPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "rollback ゾーンの hook 失敗は伝播する");
			Assert.IsTrue(handle.UnloadCalled, "成功したロードも補償 Unload される");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task Push_OnAfterLoadThrows_Propagates_AndCleansUp()
		{
			// OnAfterLoad は rollback ゾーンの最後の hook(直後から完走必須ゾーン)。境界の内側であることを確かめる。
			SetupNavigator();
			var handle = new InstantHandle();
			var presenter = new TrackingPresenter(throwOnAfterLoad: true);
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "OnAfterLoad の失敗はまだ rollback ゾーン = 伝播する");
			Assert.IsTrue(handle.UnloadCalled, "ロード済み view は補償 Unload される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "破棄経路でも OnAfterUnload が呼ばれる");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "失敗した Push は履歴に残らない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task CreatePresenterThrows_Propagates_AndNavigatorRecovers()
		{
			SetupNavigator();
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => throw new InvalidOperationException("fault injected at CreatePresenter"));

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "presenter factory の失敗は伝播する");
			Assert.IsFalse(handle.UnloadCalled, "presenter 生成は handle 生成より前なので handle は接触されない");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task CreateHandleThrows_Propagates_AndNavigatorRecovers()
		{
			SetupNavigator();
			var id = new ThrowingHandleScreenId();

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "CreateHandle の失敗は伝播する");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task Push_ConfigureThrows_Propagates_NoSideEffects_AndRecovers()
		{
			SetupNavigator();
			var factoryInvoked = false;
			var id = new ControllableScreenId(new InstantHandle(), () =>
			{
				factoryInvoked = true;
				return new NullPresenter();
			});

			Exception caught = null;
			try
			{
				await ScreenNavigator.Page.Push(id, new PushOptions
				{
					Configure = _ => throw new InvalidOperationException("fault injected at Push Configure"),
				});
			}
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Configure の失敗は伝播する");
			Assert.IsFalse(factoryInvoked, "Configure はロード前なので presenter 生成にすら到達しない");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task Push_HookThrowsSpuriousOce_InRollbackZone_CleansUpAndRecovers()
		{
			SetupNavigator();
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => new SpuriousOcePresenter("BeforeLoad"));

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "偽 OCE もそのまま伝播する(握り潰されない)");
			Assert.IsTrue(handle.UnloadCalled, "偽 OCE でもロード済み view は補償 Unload される");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "偽 OCE 後も次の Push が成立する");
		}

		[Test]
		public async Task Push_HookThrowsSpuriousOce_InCommitZone_IsAbsorbed_AndPushCompletes()
		{
			// 完走必須ゾーンは ct=None で呼ばれるため、hook が OCE を投げても
			// 「キャンセルされた」扱いにはならず、他の例外と同じく吸収して完走する。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("spurious OCE injected at BeforeEnter"));

			var id = new ControllableScreenId(new InstantHandle(), () => new SpuriousOcePresenter("BeforeEnter"));
			await ScreenNavigator.Page.Push(id);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(id, ScreenNavigator.Page.Current, "commit ゾーンの偽 OCE で遷移は中断しない");
		}

		[Test]
		public async Task Push_LoadFails_AndCompensationAlsoFails_OriginalExceptionPropagates()
		{
			SetupNavigator();
			var handle = new FaultyLoadAndUnloadHandle();
			var presenter = new FaultyAfterUnloadPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "補償の失敗で元の例外がすり替わらない");
			StringAssert.Contains("handle.Load", caught.Message, "伝播するのはロードの失敗であって補償の失敗ではない");
			Assert.IsTrue(handle.UnloadCalled, "補償 Unload は試行される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "Unload の失敗後も OnAfterUnload の補償まで進む");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "二重フォールト後も次の Push が成立する");
		}

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
