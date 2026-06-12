using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// フォールトインジェクションテスト群(<see cref="FaultInjectionTestBase"/> 派生)で共有する
	/// テストダブルと placeholder 生成ヘルパー。汎用ダブルは <see cref="ScreenTestFixtures"/> に置き、
	/// こちらは「故意に失敗させる」フォールト専用のダブルだけを集める。
	/// 以前はカテゴリ別ファイルごとに private 再定義していた(<c>FaultyLoadHandle3</c> /
	/// <c>FaultyUnloadHandle4</c> 等)が、意味が同一なものは 1 つに統合した。
	/// </summary>
	internal static class FaultInjectionFixtures
	{
		/// <summary>中身は問わないがキー形式としては有効な AssetReference(Effect prefab の placeholder)。</summary>
		public static UnityEngine.AddressableAssets.AssetReferenceGameObject NewAssetRef()
			=> new(Guid.NewGuid().ToString());

		/// <summary>_rows は private SerializeField のため Reflection で差し込む(EffectRegistryTests と同じ方式)。</summary>
		public static EffectRegistry NewRegistry(params EffectRegistry.Row[] rows)
		{
			var reg = ScriptableObject.CreateInstance<EffectRegistry>();
			typeof(EffectRegistry)
				.GetField("_rows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
				.SetValue(reg, new List<EffectRegistry.Row>(rows));
			return reg;
		}

		/// <summary>
		/// Addressables を介さず、生成済みの ScreenEffect インスタンスを掴んだ状態の EffectRunner を作る。
		/// LoadAndInstantiateAsync 後と同じ内部状態を Reflection で再現し、
		/// hook 実行時のゾーン別フォールト挙動（偽 OCE の吸収 / 本物のキャンセルの伝播）を単体で注入できるようにする。
		/// </summary>
		public static EffectRunner NewLoadedEffectRunner(ScreenEffect instance, ITransitionContext ctx)
		{
			var runner = new EffectRunner(prefabRef: null, parent: null, ctx);
			const System.Reflection.BindingFlags flags =
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
			typeof(EffectRunner).GetField("_instance", flags).SetValue(runner, instance);
			typeof(EffectRunner).GetField("_instanceGo", flags).SetValue(runner, instance.gameObject);
			return runner;
		}

		/// <summary>EffectRunner 単体テスト用の最小 ITransitionContext。</summary>
		public static ITransitionContext NewBareTransitionContext()
		{
			var store = new NavigationDataStore();
			return new TransitionContext(OperationKind.Push, from: null, to: null, store, store);
		}
	}

	// ===========================================================================
	// handle 系のフォールトダブル
	// ===========================================================================

	/// <summary>Load が失敗する handle。同期 throw と faulted UniTask の両経路を再現できる。</summary>
	internal sealed class FaultyLoadHandle : IScreenHandle
	{
		readonly bool _throwSynchronously;
		public bool UnloadCalled { get; private set; }
		public FaultyLoadHandle(bool throwSynchronously = false) => _throwSynchronously = throwSynchronously;

		public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
			=> _throwSynchronously
				? throw new InvalidOperationException("fault injected at handle.Load (sync)")
				: UniTask.FromException<IScreenViewInstance>(new InvalidOperationException("fault injected at handle.Load (async)"));

		public UniTask Unload(CancellationToken c) { UnloadCalled = true; return UniTask.CompletedTask; }
	}

	/// <summary>Unload が失敗する handle。Load は即座に NopView を返す。</summary>
	internal sealed class FaultyUnloadHandle : IScreenHandle
	{
		public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
			=> UniTask.FromResult<IScreenViewInstance>(new NopView());
		public UniTask Unload(CancellationToken c)
			=> throw new InvalidOperationException("fault injected at handle.Unload");
	}

	/// <summary>Load も補償の Unload も失敗する handle(二重フォールト用)。</summary>
	internal sealed class FaultyLoadAndUnloadHandle : IScreenHandle
	{
		public bool UnloadCalled { get; private set; }
		public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
			=> UniTask.FromException<IScreenViewInstance>(new InvalidOperationException("fault injected at handle.Load (async)"));
		public UniTask Unload(CancellationToken c)
		{
			UnloadCalled = true;
			throw new ApplicationException("fault injected at compensating Unload");
		}
	}

	/// <summary>Load が「成功」しつつ null view を返す契約違反 handle。</summary>
	internal sealed class NullViewHandle : IScreenHandle
	{
		public bool UnloadCalled { get; private set; }
		public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
			=> UniTask.FromResult<IScreenViewInstance>(null);
		public UniTask Unload(CancellationToken c) { UnloadCalled = true; return UniTask.CompletedTask; }
	}

	/// <summary>任意のオブジェクトを view インスタンスとして返す handle(As&lt;T&gt; で中身が出る)。</summary>
	internal sealed class WrappingHandle : IScreenHandle
	{
		readonly object _view;
		public WrappingHandle(object view) => _view = view;
		public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
			=> UniTask.FromResult(ScreenTesting.ViewOf(_view));
		public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
	}

	/// <summary>
	/// 1 回目の Load は即成功し、2 回目(Pop の復元ロード)だけ外部から完了を制御できる handle。
	/// 完走必須ゾーンの復元ロード中にキャンセルをぶつけるために使う。
	/// </summary>
	internal sealed class SecondLoadControllableHandle : IScreenHandle
	{
		readonly UniTaskCompletionSource<IScreenViewInstance> _secondLoad = new();
		readonly UniTaskCompletionSource _secondLoadStarted = new();
		int _loadCount;
		public UniTask SecondLoadStarted => _secondLoadStarted.Task;
		public void CompleteSecondLoad() => _secondLoad.TrySetResult(new NopView());

		public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
		{
			if (++_loadCount == 1) return UniTask.FromResult<IScreenViewInstance>(new NopView());
			_secondLoadStarted.TrySetResult();
			return _secondLoad.Task;
		}

		public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
	}

	// ===========================================================================
	// presenter 系のフォールトダブル
	// ===========================================================================

	/// <summary>指定した hook で例外を投げつつ、全 hook の呼出を記録する presenter。</summary>
	internal sealed class FaultyPresenter : IScreenPresenter
	{
		readonly string _faultAt;
		public List<string> Events { get; } = new();
		public FaultyPresenter(string faultAt = null) => _faultAt = faultAt;

		UniTask Step(string name)
		{
			Events.Add(name);
			if (name == _faultAt) throw new InvalidOperationException($"fault injected at {name}");
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnInitialize(CancellationToken c) => Step("Initialize");
		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeLoad");
		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance v, INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterLoad");
		UniTask IScreenPresenter.OnBeforeEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeEnter");
		UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterEnter");
		UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("BeforeExit");
		UniTask IScreenPresenter.OnAfterExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("AfterExit");
		UniTask IScreenPresenter.OnSuspend(CancellationToken c) => Step("Suspend");
		UniTask IScreenPresenter.OnResume(CancellationToken c) => Step("Resume");
		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c) => Step("AfterUnload");
	}

	/// <summary>全 hook の呼出を記録するだけの presenter(到達しなかったことの検証用)。</summary>
	internal sealed class RecordingPresenter : IScreenPresenter
	{
		public List<string> Events { get; } = new();

		UniTask Step(string name)
		{
			Events.Add(name);
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnInitialize(CancellationToken c) => Step("Initialize");
		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeLoad");
		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance v, INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterLoad");
		UniTask IScreenPresenter.OnBeforeEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeEnter");
		UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterEnter");
		UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("BeforeExit");
		UniTask IScreenPresenter.OnAfterExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("AfterExit");
		UniTask IScreenPresenter.OnSuspend(CancellationToken c) => Step("Suspend");
		UniTask IScreenPresenter.OnResume(CancellationToken c) => Step("Resume");
		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c) => Step("AfterUnload");
	}

	/// <summary>OnAfterUnload(補償 hook)が失敗する presenter。呼出有無も記録する。</summary>
	internal sealed class FaultyAfterUnloadPresenter : IScreenPresenter
	{
		public bool OnAfterUnloadCalled { get; private set; }

		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
		{
			OnAfterUnloadCalled = true;
			throw new InvalidOperationException("fault injected at compensating AfterUnload");
		}
	}

	/// <summary>
	/// 指定した hook で外部 CancellationTokenSource を Cancel しつつ、自分は正常完了する presenter。
	/// ct を観測しない「行儀の悪い hook」とキャンセルの競合を再現する。全 hook の呼出も記録する。
	/// </summary>
	internal sealed class CancelingPresenter : IScreenPresenter
	{
		readonly string _cancelAt;
		readonly CancellationTokenSource _cts;
		public List<string> Events { get; } = new();
		public CancelingPresenter(string cancelAt, CancellationTokenSource cts)
		{
			_cancelAt = cancelAt;
			_cts = cts;
		}

		UniTask Step(string name)
		{
			Events.Add(name);
			if (name == _cancelAt) _cts.Cancel();
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnInitialize(CancellationToken c) => Step("Initialize");
		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeLoad");
		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance v, INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterLoad");
		UniTask IScreenPresenter.OnBeforeEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeEnter");
		UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterEnter");
		UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("BeforeExit");
		UniTask IScreenPresenter.OnAfterExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("AfterExit");
		UniTask IScreenPresenter.OnSuspend(CancellationToken c) => Step("Suspend");
		UniTask IScreenPresenter.OnResume(CancellationToken c) => Step("Resume");
		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c) => Step("AfterUnload");
	}

	/// <summary>OnBeforeLoad で外部 cts を Cancel しつつ自分は正常完了する(ct を観測しない)presenter。</summary>
	internal sealed class CancelOnBeforeLoadPresenter : IScreenPresenter
	{
		readonly CancellationTokenSource _cts;
		public bool OnAfterUnloadCalled { get; private set; }
		public CancelOnBeforeLoadPresenter(CancellationTokenSource cts) => _cts = cts;

		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c)
		{
			_cts.Cancel();
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
		{
			OnAfterUnloadCalled = true;
			return UniTask.CompletedTask;
		}
	}

	/// <summary>
	/// 指定した hook で外部キャンセルなしに OperationCanceledException を投げる presenter。
	/// キャンセル経路との混線(偽 OCE の扱い)を見るために使う。
	/// </summary>
	internal sealed class SpuriousOcePresenter : IScreenPresenter
	{
		readonly string _faultAt;
		public SpuriousOcePresenter(string faultAt) => _faultAt = faultAt;

		UniTask Step(string name)
		{
			if (name == _faultAt) throw new OperationCanceledException($"spurious OCE injected at {name}");
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnInitialize(CancellationToken c) => Step("Initialize");
		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeLoad");
		UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance v, INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterLoad");
		UniTask IScreenPresenter.OnBeforeEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeEnter");
		UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterEnter");
		UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("BeforeExit");
		UniTask IScreenPresenter.OnAfterExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("AfterExit");
	}

	/// <summary>
	/// OnBeforeLoad で ct を正しく観測しながら永久に待つ presenter(割り込みが hook の await 境界に
	/// 刺さるケースの注入用)。OnAfterUnload の呼出も記録する。
	/// </summary>
	internal sealed class HangingBeforeLoadPresenter : IScreenPresenter
	{
		public bool OnAfterUnloadCalled { get; private set; }

		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c)
		{
			var tcs = new UniTaskCompletionSource();
			c.Register(() => tcs.TrySetCanceled(c));
			return tcs.Task;
		}

		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
		{
			OnAfterUnloadCalled = true;
			return UniTask.CompletedTask;
		}
	}

	/// <summary>
	/// OnBeforeExit で Started を立ててから Release まで待機する presenter。
	/// Pop / DismissAll の退場フェーズ(完走必須ゾーン)の途中に割り込み・キャンセルをぶつけるために使う。
	/// オプションで OnAfterUnload を throw させ、teardown フォールトを重ねられる。
	/// </summary>
	internal sealed class GatedExitPresenter : IScreenPresenter
	{
		readonly bool _failAfterUnload;
		readonly UniTaskCompletionSource _started = new();
		readonly UniTaskCompletionSource _release = new();
		public UniTask Started => _started.Task;
		public void Release() => _release.TrySetResult();
		public GatedExitPresenter(bool failAfterUnload = false) => _failAfterUnload = failAfterUnload;

		UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c)
		{
			_started.TrySetResult();
			return _release.Task;
		}

		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
			=> _failAfterUnload
				? throw new InvalidOperationException("fault injected at AfterUnload (gated)")
				: UniTask.CompletedTask;
	}

	/// <summary>誰も publish しない stage key(WaitForStage の timeout / キャンセル決着テスト用)。</summary>
	internal sealed class NeverPublishedStage : IStageKey { }

	/// <summary>OnBeforeLoad で NeverPublishedStage を短い timeout 付きで待つ presenter。</summary>
	internal sealed class StageWaitPresenter : IScreenPresenter
	{
		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c)
			=> x.WaitForStage<NeverPublishedStage>(c, TimeSpan.FromMilliseconds(50));
	}

	/// <summary>OnBeforeLoad で NeverPublishedStage を timeout なし・ct 付きで待つ presenter。OnAfterUnload の呼出も記録する。</summary>
	internal sealed class StageWaitCancelPresenter : IScreenPresenter
	{
		public bool OnAfterUnloadCalled { get; private set; }

		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c)
			=> x.WaitForStage<NeverPublishedStage>(c);

		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
		{
			OnAfterUnloadCalled = true;
			return UniTask.CompletedTask;
		}
	}

	/// <summary>OnAfterEnter で Redirect を発行した直後に throw する presenter。</summary>
	internal sealed class RedirectThenThrowPresenter : IScreenPresenter
	{
		readonly IScreenIdentifier _next;
		public RedirectThenThrowPresenter(IScreenIdentifier next) => _next = next;

		UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c)
		{
			ScreenNavigator.Page.Push(_next, new PushOptions { InterruptPriority = InterruptPriority.Queue }).Redirect();
			throw new InvalidOperationException("fault injected at AfterEnter (redirect origin)");
		}
	}

	/// <summary>OnAfterEnter で指定先へ Redirect を発行する presenter(自分は正常完了する)。</summary>
	internal sealed class RedirectingPresenter : IScreenPresenter
	{
		readonly IScreenIdentifier _next;
		public RedirectingPresenter(IScreenIdentifier next) => _next = next;

		UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c)
		{
			ScreenNavigator.Page.Push(_next, new PushOptions { InterruptPriority = InterruptPriority.Queue }).Redirect();
			return UniTask.CompletedTask;
		}
	}

	/// <summary>OnBeforeLoad(rollback ゾーン)で Redirect を発行した直後に throw する presenter。</summary>
	internal sealed class RedirectThenFailBeforeLoadPresenter : IScreenPresenter
	{
		readonly IScreenIdentifier _next;
		public RedirectThenFailBeforeLoadPresenter(IScreenIdentifier next) => _next = next;

		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c)
		{
			ScreenNavigator.Page.Push(_next, new PushOptions { InterruptPriority = InterruptPriority.Queue }).Redirect();
			throw new InvalidOperationException("fault injected at BeforeLoad (redirect origin)");
		}
	}

	/// <summary>OnBeforeExit で結果を書き込み、直後の OnAfterExit で throw する結果ダイアログ用 presenter。</summary>
	internal sealed class ResultThenThrowDialogPresenter : IScreenPresenter
	{
		UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c)
		{
			w.Write(new EchoResult { Text = "delivered" });
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnAfterExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c)
			=> throw new InvalidOperationException("fault injected at AfterExit (dialog)");
	}

	/// <summary>OnBeforeExit で throw し、OnAfterUnload(最後の書き込みチャンス)で結果を書く presenter。</summary>
	internal sealed class LastChanceEchoPresenter : IScreenPresenter
	{
		UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c)
			=> throw new InvalidOperationException("fault injected at BeforeExit (last-chance dialog)");

		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
		{
			w.Write(new EchoResult { Text = "last-chance" });
			return UniTask.CompletedTask;
		}
	}

	// ===========================================================================
	// view / matcher / identifier 系のフォールトダブル
	// ===========================================================================

	/// <summary>PlayEnter / PlayExit を指定で失敗させる view。</summary>
	internal sealed class FaultyAnimView : IScreenAnimatedView
	{
		readonly bool _failEnter;
		readonly bool _failExit;
		public FaultyAnimView(bool failEnter = false, bool failExit = false)
		{
			_failEnter = failEnter;
			_failExit = failExit;
		}

		public UniTask PlayEnter(CancellationToken c)
			=> _failEnter ? throw new InvalidOperationException("fault injected at PlayEnter") : UniTask.CompletedTask;
		public UniTask PlayExit(CancellationToken c)
			=> _failExit ? throw new InvalidOperationException("fault injected at PlayExit") : UniTask.CompletedTask;
	}

	/// <summary>
	/// OnBeforeLoad で外部キャンセルなしに OperationCanceledException を投げる ScreenEffect。
	/// EffectRunner のゾーン別 OCE 取り扱い(偽 OCE は装飾失敗として吸収される契約)の注入用。
	/// 呼出回数も記録し、失敗後の残 hook skip を観測できるようにする。
	/// </summary>
	internal sealed class SpuriousOceEffect : ScreenEffect
	{
		public int BeforeLoadCalls { get; private set; }
		public int AfterLoadCalls { get; private set; }

		public override UniTask OnBeforeLoad(ITransitionContext ctx, CancellationToken ct)
		{
			BeforeLoadCalls++;
			throw new OperationCanceledException("spurious OCE injected at Effect.OnBeforeLoad");
		}

		public override UniTask OnAfterLoad(ITransitionContext ctx, CancellationToken ct)
		{
			AfterLoadCalls++;
			return UniTask.CompletedTask;
		}
	}

	/// <summary>OnBeforeLoad で ct を正しく観測して OCE を投げる行儀の良い ScreenEffect(本物のキャンセル経路の注入用)。</summary>
	internal sealed class CtObservingEffect : ScreenEffect
	{
		public override UniTask OnBeforeLoad(ITransitionContext ctx, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			return UniTask.CompletedTask;
		}
	}

	/// <summary>Match が必ず throw する matcher(Effect 解決失敗の注入用)。</summary>
	internal sealed class ThrowingMatcher : ScreenMatcher
	{
		public override bool Match(IScreenIdentifier id, ITransitionContext ctx)
			=> throw new InvalidOperationException("fault injected at Matcher.Match");
	}

	/// <summary>CreateHandle が必ず throw する identifier(factory 境界のフォールト注入用)。</summary>
	internal sealed record ThrowingHandleScreenId : ScreenIdentifier
	{
		public override IScreenHandle CreateHandle(ScreenServices s)
			=> throw new InvalidOperationException("fault injected at CreateHandle");
		public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
	}

	/// <summary>Load が必ず失敗する、結果を返すダイアログの identifier(PushAndAwait 用)。</summary>
	internal sealed record FaultyLoadDialogId : ScreenIdentifier<EchoResult>
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => new FaultyLoadHandle();
		public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
	}

	/// <summary>任意の handle を差し込める、結果を返すダイアログの identifier(PushAndAwait のキャンセル系テスト用)。</summary>
	internal sealed record ControllableDialogId(IScreenHandle Handle) : ScreenIdentifier<EchoResult>
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => Handle;
		public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
	}

	/// <summary>結果書き込み後に退場 hook が落ちる、結果を返すダイアログの identifier。</summary>
	internal sealed record FaultyExitEchoDialogId : ScreenIdentifier<EchoResult>
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
		public override IScreenPresenter CreatePresenter(ScreenServices s) => new ResultThenThrowDialogPresenter();
	}

	/// <summary>退場 hook が落ちつつ OnAfterUnload で結果を書く、結果を返すダイアログの identifier。</summary>
	internal sealed record LastChanceEchoDialogId : ScreenIdentifier<EchoResult>
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
		public override IScreenPresenter CreatePresenter(ScreenServices s) => new LastChanceEchoPresenter();
	}

	/// <summary>OnBeforeExit で結果を書き、teardown（OnAfterUnload）で throw する presenter（二重 teardown フォールト用）。</summary>
	internal sealed class ResultThenFaultyTeardownPresenter : IScreenPresenter
	{
		UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c)
		{
			w.Write(new EchoResult { Text = "delivered" });
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
			=> throw new InvalidOperationException("fault injected at AfterUnload (teardown dialog)");
	}

	/// <summary>退場で結果を書いた後、handle.Unload と OnAfterUnload の両方が落ちる、結果を返すダイアログの identifier。</summary>
	internal sealed record DoubleTeardownFaultDialogId : ScreenIdentifier<EchoResult>
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => new FaultyUnloadHandle();
		public override IScreenPresenter CreatePresenter(ScreenServices s) => new ResultThenFaultyTeardownPresenter();
	}
}
