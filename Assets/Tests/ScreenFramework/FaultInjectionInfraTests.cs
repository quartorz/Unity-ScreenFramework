using System;
using System.Collections.Generic;
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
	using static FaultInjectionFixtures;

	/// <summary>
	/// フォールトインジェクションテスト: 遷移本筋を支える周辺機構の注入点。装飾(Effect の Matcher 例外 /
	/// EffectRoot 未設定 / prefab ロード失敗 / hook の偽 OCE を含むゾーン別フォールト)と
	/// stage signal(publish されない待ちの timeout 決着)は吸収して遷移を続行する。
	/// History.Edit の遅延適用フォールト、遷移イベント購読者の例外(成功時・失敗時の両方)、レイヤー間の
	/// フォールト隔離、Redirect(hook 内リダイレクト)、失敗/キャンセルされた遷移のイベント通知
	/// (Succeeded=false)、Shutdown / Initialize の原子性と進行中遷移との競合を扱う。
	/// commit ゾーンの例外は Debug.LogException されるので該当テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionInfraTests : FaultInjectionTestBase
	{
		// ===========================================================================
		// Effect 機構(装飾の失敗は吸収して遷移続行する契約)
		// ===========================================================================

		[Test]
		public async Task ResolveThrows_IsAbsorbed_AndTransitionContinues()
		{
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at EffectRegistry\\.Resolve"));
			SetupNavigatorWithPageRegistry(new ThrowingEffectRegistry());

			var id = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(id);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(id, ScreenNavigator.Page.Current, "Resolve の例外で遷移本筋は止まらない");
		}

		[Test]
		public async Task EffectMatchedButEffectRootMissing_WarnsAndSkips_TransitionContinues()
		{
			LogAssert.Expect(LogType.Warning, new Regex("EffectHost is null"));
			SetupNavigatorWithPageRegistry(new StubMatchingEffectRegistry(NewAssetRef()));   // EffectHost は意図的に未設定

			var id = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(id);

			Assert.AreSame(id, ScreenNavigator.Page.Current, "Effect の設定不備で遷移本筋は止まらない");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task EffectPrefabLoadFails_IsAbsorbed_AndTransitionContinues()
		{
			// 形式上は有効だが実在しない GUID の AssetReference を EffectHost 付きでマッチさせ、
			// prefab の Load/Instantiate 失敗が吸収されることを見る。
			// Addressables 自体が出すエラーログは本数・文言が環境依存なので個別 Expect せず一括で無視する。
			var effectHost = new GameObject("EffectHost").AddComponent<EffectHost>();
			LogAssert.ignoreFailingMessages = true;
			try
			{
				SetupNavigatorWithPageRegistry(new StubMatchingEffectRegistry(NewAssetRef()), effectHost);

				var id = new MarkerScreenId("A");
				await ScreenNavigator.Page.Push(id);

				Assert.AreSame(id, ScreenNavigator.Page.Current, "Effect prefab のロード失敗で遷移本筋は止まらない");
				Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			}
			finally
			{
				LogAssert.ignoreFailingMessages = false;
				UnityEngine.Object.DestroyImmediate(effectHost.gameObject);
			}
		}

		// ===========================================================================
		// EffectRunner のゾーン別 OCE 取り扱い(装飾は本筋を止めない契約の境界)
		// Addressables を介した hook 注入は EditMode では不可能なため、ロード完了後の
		// 内部状態を Reflection で再現した runner に直接注入する(FaultInjectionFixtures.NewLoadedEffectRunner)。
		// ===========================================================================

		[Test]
		public async Task EffectHookThrowsSpuriousOce_InRollbackZone_IsAbsorbed_AndRemainingHooksSkip()
		{
			// ct 起因でない偽 OCE は「装飾の失敗」であって遷移のキャンセルではない。
			// rollback ゾーンでも吸収され(Effect は即 Destroy + disabled)、呼び出し側へ伝播しない。
			LogAssert.Expect(LogType.Exception, new Regex("spurious OCE injected at Effect\\.OnBeforeLoad"));
			var go = new GameObject("SpuriousOceEffect");
			var eff = go.AddComponent<SpuriousOceEffect>();
			try
			{
				var runner = NewLoadedEffectRunner(eff, NewBareTransitionContext());

				await runner.OnBeforeLoad(EffectZone.Rollback, CancellationToken.None);   // throw しないのが契約

				Assert.AreEqual(1, eff.BeforeLoadCalls);
				await runner.OnAfterLoad(EffectZone.Rollback, CancellationToken.None);
				Assert.AreEqual(0, eff.AfterLoadCalls, "偽 OCE で disabled になった後の hook は skip される");
			}
			finally
			{
				if (go != null) UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public async Task EffectHookThrowsSpuriousOce_InCommitZone_IsAbsorbed_AndDestroyIsDeferredToFinish()
		{
			// 完走必須ゾーンの Effect 失敗は「残 hook skip のみ・Destroy は遷移完了(Finish)まで遅延」の契約。
			LogAssert.Expect(LogType.Exception, new Regex("spurious OCE injected at Effect\\.OnBeforeLoad"));
			var go = new GameObject("SpuriousOceEffect");
			var eff = go.AddComponent<SpuriousOceEffect>();
			try
			{
				var runner = NewLoadedEffectRunner(eff, NewBareTransitionContext());

				await runner.OnBeforeLoad(EffectZone.Commit, CancellationToken.None);

				Assert.IsTrue(go != null, "commit ゾーンの失敗では Destroy は遷移完了まで遅延される");
				await runner.OnAfterLoad(EffectZone.Commit, CancellationToken.None);
				Assert.AreEqual(0, eff.AfterLoadCalls, "失敗後の残 hook は skip される");

				runner.Finish();
				Assert.IsTrue(go == null, "Finish で遅延されていた Destroy が実行される");
			}
			finally
			{
				if (go != null) UnityEngine.Object.DestroyImmediate(go);
			}
		}

		[Test]
		public async Task EffectHookObservesRealCancel_InRollbackZone_OcePropagates()
		{
			// 本物のキャンセル(ct 起因)は巻き戻しの一部なので、rollback ゾーンでは従来どおり伝播する。
			var go = new GameObject("CtObservingEffect");
			var eff = go.AddComponent<CtObservingEffect>();
			try
			{
				var runner = NewLoadedEffectRunner(eff, NewBareTransitionContext());
				using var cts = new CancellationTokenSource();
				cts.Cancel();

				Exception caught = null;
				try { await runner.OnBeforeLoad(EffectZone.Rollback, cts.Token); }
				catch (Exception e) { caught = e; }

				Assert.IsInstanceOf<OperationCanceledException>(caught, "ct 起因の OCE は rollback のため伝播する");
			}
			finally
			{
				if (go != null) UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ===========================================================================
		// stage signal(publish されない stage への待ちが timeout で抜けられる)
		// ===========================================================================

		[Test]
		public async Task Push_HookWaitsForStageNeverPublished_TimesOutAndRollsBack()
		{
			SetupNavigator();
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => new StageWaitPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<TimeoutException>(caught, "publish されない stage の待ちは timeout で決着する");
			Assert.IsTrue(handle.UnloadCalled, "timeout した遷移も補償 Unload される");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "timeout 後も次の Push が成立する");
		}

		// ===========================================================================
		// History.Edit のフォールト(無音編集は遷移本筋にも以後の操作にも影響しない)
		// ===========================================================================

		[Test]
		public async Task HistoryEdit_DeferredActionThrows_IsAbsorbed_AndNavigatorRemainsUsable()
		{
			// 遷移中に積まれた Edit はチェーン完了後に適用される。その適用が throw しても
			// 完了済みの遷移と以後の操作は壊れない。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at History\\.Edit"));

			var gated = new GatedPresenter();
			var pushTask = ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => gated));
			await gated.Started;   // 遷移中(完走必須ゾーン)
			ScreenNavigator.Page.History.Edit(_ => throw new InvalidOperationException("fault injected at History.Edit"));
			gated.Release();

			await pushTask;

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "編集の失敗で遷移本筋は壊れない");

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "編集の失敗後も次の操作が成立する");
		}

		[Test]
		public async Task HistoryEdit_RemovedEntryUnloadThrows_EditStillApplies()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover);   // 下の A を生きたまま編集で外す
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at handle\\.Unload"));

			await ScreenNavigator.Page.Push(new ControllableScreenId(new FaultyUnloadHandle()));
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);

			ScreenNavigator.Page.History.Edit(e => e.RemoveAt(0));

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "破棄処理の失敗で編集適用が止まらない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "編集後も次の操作が成立する");
		}

		[Test]
		public async Task HistoryEdit_DeferredDuringFailedTransition_IsStillApplied()
		{
			SetupNavigator();
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);

			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var pushX = ScreenNavigator.Page.Push(new ControllableScreenId(new ControllableHandle(source)));
			Assert.IsTrue(ScreenNavigator.Page.IsTransitioning, "この時点で遷移中である前提");

			ScreenNavigator.Page.History.Edit(e => e.RemoveAt(0));   // 遷移中なので遅延される
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "遷移中の Edit は即時適用されない");

			source.TrySetException(new InvalidOperationException("fault injected at deferred-edit load"));
			Exception caught = null;
			try { await pushX; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "遷移が失敗しても遅延された Edit は適用される");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "編集適用後も次の Push が成立する");
		}

		// ===========================================================================
		// 遷移イベント購読者のフォールト / 失敗・キャンセルされた遷移の通知
		// ===========================================================================

		[Test]
		public async Task TransitionEventHandlersThrow_TransitionStillSucceeds()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("observer fault at start"));
			LogAssert.Expect(LogType.Exception, new Regex("observer fault at end"));

			Action<ScreenTransitionEvent> onStart = _ => throw new InvalidOperationException("observer fault at start");
			Action<ScreenTransitionEvent> onEnd = _ => throw new InvalidOperationException("observer fault at end");
			ScreenNavigator.Page.OnTransitionStart += onStart;
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			try
			{
				var idA = new MarkerScreenId("A");
				await ScreenNavigator.Page.Push(idA);

				Assert.AreSame(idA, ScreenNavigator.Page.Current, "購読者の例外は遷移本筋に影響しない");
				Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			}
			finally
			{
				// TearDown の Shutdown(DismissAll) でも発火するので、テスト末尾で必ず外す
				ScreenNavigator.Page.OnTransitionStart -= onStart;
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}
		}

		[Test]
		public async Task FailedPush_StillFiresTransitionEnd_WithSucceededFalse()
		{
			SetupNavigator();
			var ends = new List<ScreenTransitionEvent>();
			Action<ScreenTransitionEvent> onEnd = ends.Add;
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			try
			{
				Exception caught = null;
				try { await ScreenNavigator.Page.Push(new ControllableScreenId(new FaultyLoadHandle())); }
				catch (Exception e) { caught = e; }

				Assert.IsInstanceOf<InvalidOperationException>(caught);
				Assert.AreEqual(1, ends.Count, "失敗した遷移でも OnTransitionEnd は発火する");
				Assert.IsFalse(ends[0].Succeeded, "ロールバックされた遷移は Succeeded=false で通知される");
				Assert.AreEqual(ScreenTransitionKind.Push, ends[0].Kind);
			}
			finally
			{
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}
		}

		[Test]
		public async Task CanceledPush_FiresTransitionEnd_WithSucceededFalse()
		{
			SetupNavigator();
			var ends = new List<ScreenTransitionEvent>();
			Action<ScreenTransitionEvent> onEnd = ends.Add;
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			try
			{
				using var cts = new CancellationTokenSource();
				var source = new UniTaskCompletionSource<IScreenViewInstance>();
				var pushTask = ScreenNavigator.Page.Push(new ControllableScreenId(new ControllableHandle(source)), ct: cts.Token);
				cts.Cancel();

				Exception caught = null;
				try { await pushTask; }
				catch (Exception e) { caught = e; }

				Assert.IsInstanceOf<OperationCanceledException>(caught);
				Assert.AreEqual(1, ends.Count, "キャンセルされた遷移でも OnTransitionEnd は発火する");
				Assert.IsFalse(ends[0].Succeeded, "キャンセルされた遷移は Succeeded=false で通知される");
				Assert.AreEqual(ScreenTransitionKind.Push, ends[0].Kind);
			}
			finally
			{
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}
		}

		[Test]
		public async Task FailedPush_EndObserverThrows_OriginalExceptionStillPropagates()
		{
			// OnTransitionEnd は finally から発火される。購読者の例外を素通しすると
			// 元の失敗(load 例外)を握り潰してすり替えてしまうため、吸収されることを固定する。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("observer fault at end"));
			Action<ScreenTransitionEvent> onEnd = _ => throw new InvalidOperationException("observer fault at end");
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			try
			{
				Exception caught = null;
				try { await ScreenNavigator.Page.Push(new ControllableScreenId(new FaultyLoadHandle())); }
				catch (Exception e) { caught = e; }

				Assert.IsInstanceOf<InvalidOperationException>(caught, "失敗した遷移の例外は購読者の例外にすり替わらない");
				StringAssert.Contains("handle.Load", caught.Message, "伝播するのは load の失敗であって購読者の失敗ではない");
				Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			}
			finally
			{
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}
		}

		// ===========================================================================
		// レイヤー間のフォールト隔離
		// ===========================================================================

		[Test]
		public async Task PageLayerFault_DoesNotAffectDialogLayer()
		{
			SetupNavigator();
			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var pagePush = ScreenNavigator.Page.Push(new ControllableScreenId(new ControllableHandle(source)));

			// Page がロード中(遷移中)でも Dialog レイヤーは独立して操作できる
			var idDialog = new MarkerScreenId("D");
			await ScreenNavigator.Dialog.Push(idDialog);
			Assert.AreSame(idDialog, ScreenNavigator.Dialog.Current, "Page の遷移中でも Dialog の Push が成立する");
			Assert.IsTrue(ScreenNavigator.Page.IsTransitioning, "Page 側はまだ遷移中のまま");

			// Page 側をフォールトさせても Dialog 側のスタックは無傷
			source.TrySetException(new InvalidOperationException("fault injected at page load"));
			Exception caught = null;
			try { await pagePush; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught);
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.AreEqual(1, ScreenNavigator.Dialog.History.Count, "Page のフォールトは Dialog に波及しない");
			Assert.AreSame(idDialog, ScreenNavigator.Dialog.Current);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールトした Page 側も復帰可能");
		}

		// ===========================================================================
		// Redirect(hook 内リダイレクト)とフォールトの組み合わせ
		// ===========================================================================

		[Test]
		public async Task Redirect_IssuedFromThrowingHook_StillExecutes()
		{
			// commit ゾーンの hook が Redirect を発行した直後に throw しても、
			// 現在の遷移は完走し(吸収)、リダイレクトはその後に実行される。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterEnter \\(redirect origin\\)"));
			var idNext = new MarkerScreenId("Next");
			var id = new ControllableScreenId(new InstantHandle(), () => new RedirectThenThrowPresenter(idNext));

			await ScreenNavigator.Page.Push(id);

			await UniTask.WaitUntil(() => ReferenceEquals(ScreenNavigator.Page.Current, idNext))
				.Timeout(TimeSpan.FromSeconds(5));
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "発行元 hook の失敗でリダイレクトが失われない");
		}

		[Test]
		public async Task Redirect_TargetLoadFails_NavigatorStaysUsable()
		{
			// リダイレクトは fire-and-forget なので失敗は呼び出し元に返らない。
			// 契約は「Navigator が壊れない + OnTransitionEnd(Succeeded=false) で観測できる」まで。
			// Forget 経由の未観測例外ログは経路依存なので一括で無視する。
			SetupNavigator();
			var idTarget = new ControllableScreenId(new FaultyLoadHandle());
			var id = new ControllableScreenId(new InstantHandle(), () => new RedirectingPresenter(idTarget));
			var failedEnds = 0;
			Action<ScreenTransitionEvent> onEnd = e => { if (!e.Succeeded) Interlocked.Increment(ref failedEnds); };
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			LogAssert.ignoreFailingMessages = true;
			try
			{
				await ScreenNavigator.Page.Push(id);

				await UniTask.WaitUntil(() => failedEnds > 0).Timeout(TimeSpan.FromSeconds(5));

				Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "失敗したリダイレクトは積まれない");
				Assert.AreSame(id, ScreenNavigator.Page.Current, "発行元の画面はそのまま");
				Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

				var idB = new MarkerScreenId("B");
				await ScreenNavigator.Page.Push(idB);
				Assert.AreSame(idB, ScreenNavigator.Page.Current, "リダイレクト失敗後も次の Push が成立する");
			}
			finally
			{
				LogAssert.ignoreFailingMessages = false;
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}
		}

		[Test]
		public async Task Redirect_IssuedFromFailedRollbackHook_StillExecutes()
		{
			// rollback ゾーンの hook が Redirect(Queue)を発行した直後に throw しても、
			// 発行元の遷移は補償付きで巻き戻り(失敗は呼び出し側へ伝播)、
			// リダイレクトは FIFO の契約どおり先行の失敗を引き継がず実行される。
			SetupNavigator();
			var idNext = new MarkerScreenId("Next");
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => new RedirectThenFailBeforeLoadPresenter(idNext));

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "発行元の失敗は呼び出し側へ伝播する");
			Assert.IsTrue(handle.UnloadCalled, "発行元のロード済み view は補償 Unload される");

			await UniTask.WaitUntil(() => ReferenceEquals(ScreenNavigator.Page.Current, idNext))
				.Timeout(TimeSpan.FromSeconds(5));
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "発行元のロールバックでリダイレクトが失われない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		// ===========================================================================
		// Shutdown / Initialize と進行中遷移・pending awaiter・再 Initialize の組み合わせ
		// ===========================================================================

		[Test]
		public async Task Shutdown_WithThrowingExitHook_CompletesAndAllowsReinitialize()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeHide"));
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("BeforeHide")));

			await ScreenNavigator.Shutdown();

			Assert.IsNull(ScreenNavigator.Page, "hook の失敗があっても静的参照は畳まれる");

			// 再 Initialize して通常の操作ができる
			DestroyContainer(_pageContainer);
			SetupNavigator();
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "Shutdown 後の再 Initialize で通常運転に戻れる");
		}

		[Test]
		public async Task Shutdown_DuringCommitZonePush_PushCompletes_ThenEverythingIsCleared()
		{
			SetupNavigator();
			var nav = ScreenNavigator.Page;
			var gated = new GatedPresenter();
			var id = new ControllableScreenId(new InstantHandle(), () => gated);

			var pushTask = ScreenNavigator.Page.Push(id);
			await gated.Started;   // OnBeforeShow = 完走必須ゾーンで停止中

			var shutdownTask = ScreenNavigator.Shutdown();
			Assert.IsNull(ScreenNavigator.Page, "静的参照は Shutdown 呼び出しと同期に外れる");

			gated.Release();
			var entry = await pushTask;   // 完走必須ゾーンの遷移は Shutdown に殺されず完走する
			Assert.IsNotNull(entry, "進行中だった Push は正常に決着する");

			await shutdownTask;
			Assert.AreEqual(0, nav.History.Count, "完走を待ってから全画面が畳まれる");
			Assert.IsFalse(nav.IsTransitioning);

			// 畳み終わった後は通常どおり再 Initialize できる
			DestroyContainer(_pageContainer);
			SetupNavigator();
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "Shutdown 後の再 Initialize で通常運転に戻れる");
		}

		[Test]
		public async Task Shutdown_DuringRollbackZonePush_PusherSettlesOce_AndLayerIsCleared()
		{
			// Shutdown は各レイヤーに DismissAll(Preempt)を発行する。ロード中(rollback ゾーン)の
			// Push は殺されて補償付きで OCE 決着し、Shutdown はハングせず畳み切る。
			SetupNavigator();
			var nav = ScreenNavigator.Page;
			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var handle = new ControllableHandle(source);
			var presenter = new TrackingPresenter();
			var pushTask = nav.Push(new ControllableScreenId(handle, () => presenter));

			var shutdownTask = ScreenNavigator.Shutdown();
			Assert.IsNull(ScreenNavigator.Page, "静的参照は Shutdown 呼び出しと同期に外れる");

			Exception caught = null;
			try { await pushTask; }
			catch (Exception e) { caught = e; }
			await shutdownTask;

			Assert.IsInstanceOf<OperationCanceledException>(caught, "ロード中の Push は Shutdown に preempt されて OCE で決着する");
			Assert.IsTrue(handle.UnloadCalled, "殺されたロードは補償 Unload される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "破棄経路でも OnAfterUnload が呼ばれる");
			Assert.AreEqual(0, nav.History.Count, "敗者は積まれず、レイヤーは畳み切られる");
			Assert.IsFalse(nav.IsTransitioning);
		}

		[Test]
		public async Task Shutdown_PendingPushAndAwait_AwaiterSettlesWithOce()
		{
			SetupNavigator();
			await ScreenNavigator.Page.Push(new MarkerScreenId("Base"));
			var resultTask = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("never"));

			await ScreenNavigator.Shutdown();

			Exception caught = null;
			try { await resultTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "Shutdown に畳まれた awaiter は OCE で決着する(ハングしない)");
			Assert.IsNull(ScreenNavigator.Page, "静的参照は畳まれている");
		}

		[Test]
		public async Task Reinitialize_BeforeShutdownCompletes_IsUnaffectedByOldTeardownFault()
		{
			// Shutdown は静的参照を同期的に外すので、退場演出が終わる前でも再 Initialize できる契約。
			// 旧レイヤーの teardown フォールト(OnAfterUnload throw)が新 Navigator に波及しないことを見る。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterUnload \\(gated\\)"));
			var oldNav = ScreenNavigator.Page;
			var oldContainer = _pageContainer;
			await ScreenNavigator.Page.Push(new MarkerScreenId("Old"));
			var gated = new GatedExitPresenter(failAfterUnload: true);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => gated));

			var shutdownTask = ScreenNavigator.Shutdown();
			await gated.Started;   // 旧レイヤーは退場 hook で停止中(畳み切っていない)

			SetupNavigator();   // 静的参照は外れているので即 Initialize できる
			var idNew = new MarkerScreenId("New");
			await ScreenNavigator.Page.Push(idNew);
			Assert.AreSame(idNew, ScreenNavigator.Page.Current, "旧レイヤーの畳み中でも新 Navigator は通常運転できる");

			gated.Release();
			await shutdownTask;

			Assert.AreEqual(0, oldNav.History.Count, "teardown フォールトがあっても旧レイヤーは畳み切られる");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "旧レイヤーのフォールトは新 Navigator に波及しない");
			Assert.AreSame(idNew, ScreenNavigator.Page.Current);

			DestroyContainer(oldContainer);
		}

		[Test]
		public async Task InitializeTwice_Throws_AndExistingNavigatorsRemainUsable()
		{
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);

			var extraPage = NewContainer("ExtraPageRoot");
			var extraDialog = NewContainer("ExtraDialogRoot");
			var extraSys = NewContainer("ExtraSysRoot");
			try
			{
				Assert.Throws<InvalidOperationException>(() =>
					ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
					{
						Page = NewLayer(extraPage),
						Dialog = NewLayer(extraDialog),
						SystemDialog = NewLayer(extraSys),
					}), "初期化済みのままの再 Initialize は例外");
			}
			finally
			{
				DestroyContainer(extraPage);
				DestroyContainer(extraDialog);
				DestroyContainer(extraSys);
			}

			Assert.AreSame(idA, ScreenNavigator.Page.Current, "失敗した再 Initialize は既存 Navigator を壊さない");
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "既存 Navigator は引き続き操作できる");
		}

		[Test]
		public async Task Initialize_PartialValidationFailure_LeavesNothingInitialized()
		{
			// Dialog レイヤーの Container 欠落で Initialize が途中失敗するケース。
			// 失敗した Initialize が「初期化済み」状態を残すと、成功していないのに
			// Shutdown を要求される自己矛盾になるため、全静的参照は null のままであるべき。
			var pageC = NewContainer("AtomicPageRoot");
			var sysC = NewContainer("AtomicSysRoot");
			try
			{
				Assert.Throws<ArgumentException>(() =>
					ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
					{
						Page = NewLayer(pageC),
						Dialog = new ScreenLayerConfig { Container = null },   // 検証で throw する
						SystemDialog = NewLayer(sysC),
					}), "Container 欠落は Initialize の時点で fail-fast する");

				Assert.IsNull(ScreenNavigator.Page, "失敗した Initialize は Page を初期化済みにしない");
				Assert.IsNull(ScreenNavigator.Dialog, "失敗した Initialize は Dialog を初期化済みにしない");
				Assert.IsNull(ScreenNavigator.SystemDialog, "失敗した Initialize は SystemDialog を初期化済みにしない");

				// 部分状態が残っていなければ、Shutdown を挟まず正しい設定での Initialize がそのまま成立する。
				SetupNavigator();
				var idA = new MarkerScreenId("A");
				await ScreenNavigator.Page.Push(idA);
				Assert.AreSame(idA, ScreenNavigator.Page.Current, "失敗した Initialize の後も正しい Initialize で復帰できる");
			}
			finally
			{
				DestroyContainer(pageC);
				DestroyContainer(sysC);
			}
		}

		[Test]
		public async Task Pop_ResolveThrows_IsAbsorbed_AndPopCompletes()
		{
			// Effect の解決は Pop でも走る。commit ゾーン(Pop は全段完走必須)でも Resolve の例外は吸収される。
			// Resolve は Push A / Push B / Pop の遷移ごとに 1 回ずつ呼ばれ、毎回 throw が吸収される。
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at EffectRegistry\\.Resolve"));
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at EffectRegistry\\.Resolve"));
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at EffectRegistry\\.Resolve"));
			SetupNavigatorWithPageRegistry(new ThrowingEffectRegistry());

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "Matcher の例外で Pop が中断しない");
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task TransitionEndObserver_IssuingQueuedOp_RunsAfterCurrent_AndStackStaysCoherent()
		{
			// 観測者(購読者)は例外だけでなく「遷移 API を呼び返す」こともできてしまう。
			// FireEnd は遷移本体の内側で同期発火するため、そこから発行された操作は FIFO チェーンに
			// 積まれて現在の遷移完了後に実行され、本筋の bookkeeping を壊さないことを固定する。
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);

			var popTask = UniTask.CompletedTask;
			var issued = false;
			Action<ScreenTransitionEvent> onEnd = _ =>
			{
				if (issued) return;
				issued = true;
				popTask = ScreenNavigator.Page.Pop(new PopOptions { InterruptPriority = InterruptPriority.Queue });
			};
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			try
			{
				await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
				await popTask;   // 観測者発行の Pop は Push 完了後に実行される
			}
			finally
			{
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}

			Assert.AreSame(idA, ScreenNavigator.Page.Current, "観測者発行の Pop が Push 完了後に正しく適用される");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}
	}
}
