using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ScreenFramework
{
	public sealed class ScreenNavigatorImpl : IScreenNavigator
	{
		readonly ScreenServices _services;
		readonly ScreenLayerConfig _config;
		readonly ScreenHistory _history = new();
		readonly List<LiveEntry> _live = new();   // parallel to _history; null = dormant

		// Preempt 用：現在進行中の遷移の CTS と完了シグナル
		// UniTask は単一 await 設計のため、複数の後続が完了を観測できるよう
		// UniTaskCompletionSource を完了シグナルとして使う
		CancellationTokenSource _currentCts;
		UniTaskCompletionSource _currentDoneSignal; // null なら走っていない

		public IScreenHistory History => _history;
		public IScreenIdentifier Current => _history.Current;
		public bool IsTransitioning { get; private set; }

		public event Action<ScreenTransitionEvent> OnTransitionStart;
		public event Action<ScreenTransitionEvent> OnTransitionEnd;

		public ScreenNavigatorImpl(ScreenServices services, ScreenLayerConfig config)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_config = config ?? throw new ArgumentNullException(nameof(config));
		}

		// ===========================================================================
		// 公開 API
		// ===========================================================================

		public UniTask Push(IScreenIdentifier id, PushOptions opt = default, CancellationToken ct = default)
		{
			if (id == null) throw new ArgumentNullException(nameof(id));
			return Run(opt.InterruptPriority, ct, async myCt =>
				await PushCore(id, opt, resultSource: null, myCt));
		}

		public async UniTask<TResult> PushAndAwait<TResult>(ScreenIdentifier<TResult> id, PushOptions opt = default, CancellationToken ct = default)
			where TResult : IScreenData
		{
			if (id == null) throw new ArgumentNullException(nameof(id));

			// このエントリ専用の結果完了ソース。ExitPreviousAsync で TrySetResult される。
			var tcs = new UniTaskCompletionSource<IScreenDataReader>();

			// Push 自体は通常通り Run。tcs を PushCore に持ち込んで entry.ResultSource に貼る。
			await Run(opt.InterruptPriority, ct, async myCt =>
				await PushCore(id, opt, resultSource: tcs, myCt));

			// 自分のエントリが閉じるのを待つ。preempt/DismissAll/Reset/Change で死んだら
			// TrySetCanceled されて OperationCanceledException で抜ける。
			var reader = await tcs.Task;
			return reader.TryRead<TResult>(out var result) ? result : default;
		}

		public UniTask Pop(PopOptions opt = default, CancellationToken ct = default)
		{
			return Run(opt.InterruptPriority, ct, async myCt =>
				await PopCore(opt, myCt));
		}

		public UniTask Replace(IScreenIdentifier id, ReplaceOptions opt = default, CancellationToken ct = default)
		{
			if (id == null) throw new ArgumentNullException(nameof(id));
			return Run(opt.InterruptPriority, ct, async myCt =>
				await ReplaceCore(id, opt, myCt));
		}

		public async UniTask Change(IScreenIdentifier id, ChangeOptions opt = default, CancellationToken ct = default)
		{
			// 履歴を破棄して新画面 1 枚にする
			await ClearAllExceptCurrentAsync(ct);
			await Replace(id, new ReplaceOptions
			{
				Data = opt.Data,
				TransitionDirector = opt.TransitionDirector,
				CachePolicyOverride = opt.CachePolicyOverride,
				ModalOverride = opt.ModalOverride,
				InterruptPriority = opt.InterruptPriority,
			}, ct);
		}

		public async UniTask Reset(IScreenIdentifier id, ResetOptions opt = default, CancellationToken ct = default)
		{
			await DismissAll(ct);
			await Push(id, new PushOptions
			{
				Data = opt.Data,
				TransitionDirector = opt.TransitionDirector,
				CachePolicyOverride = opt.CachePolicyOverride,
				ModalOverride = opt.ModalOverride,
				InterruptPriority = opt.InterruptPriority,
			}, ct);
		}

		public async UniTask PopTo(Func<IScreenIdentifier, bool> predicate, PopToOptions opt = default, CancellationToken ct = default)
		{
			if (predicate == null) throw new ArgumentNullException(nameof(predicate));
			var targetIndex = -1;
			for (var i = _history.Count - 1; i >= 0; i--)
			{
				if (predicate(_history[i])) { targetIndex = i; break; }
			}
			if (targetIndex < 0 || targetIndex == _history.Count - 1) return;

			for (var i = _history.Count - 2; i > targetIndex; i--)
			{
				if (_live[i] != null)
				{
					await ExitPreviousAsync(_live[i], ScreenCacheMode.DestroyOnCover, isPop: true, CancellationToken.None);
					DestroyBlockerIfAny(_live[i]);
				}
				_live.RemoveAt(i);
				_history.RemoveAtInternal(i);
			}

			await Pop(new PopOptions
			{
				TransitionDirector = opt.TransitionDirector,
				InterruptPriority = opt.InterruptPriority,
			}, ct);
		}

		public async UniTask DismissAll(CancellationToken ct = default)
		{
			while (_history.Count > 0)
			{
				var top = _live[_live.Count - 1];
				if (top != null)
				{
					await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: true, CancellationToken.None);
					DestroyBlockerIfAny(top);
				}
				_live.RemoveAt(_live.Count - 1);
				_history.PopCurrent();
			}
		}

		// ===========================================================================
		// Preempt スケジューラ
		// ===========================================================================

		async UniTask Run(InterruptPriority priority, CancellationToken externalCt, Func<CancellationToken, UniTask> body)
		{
			// 直前の遷移の状態をキャプチャ
			var prevCts = _currentCts;
			var prevDone = _currentDoneSignal;

			if (priority == InterruptPriority.Preempt)
			{
				prevCts?.Cancel();
			}
			// 直前の遷移の完走を待つ（完了シグナル）
			if (prevDone != null)
			{
				try { await prevDone.Task; }
				catch (OperationCanceledException) { /* preempt されただけ */ }
				catch { /* 前遷移のエラーは握り潰す（自分とは別件） */ }
			}

			var myCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
			var myDone = new UniTaskCompletionSource();
			_currentCts = myCts;
			_currentDoneSignal = myDone;
			IsTransitioning = true;

			try
			{
				await body(myCts.Token);
				myDone.TrySetResult();
			}
			catch (OperationCanceledException)
			{
				myDone.TrySetCanceled();
				throw;
			}
			catch (Exception ex)
			{
				myDone.TrySetException(ex);
				throw;
			}
			finally
			{
				// 自分が最新のままなら状態クリア（後続に preempt されていたら触らない）
				if (ReferenceEquals(_currentCts, myCts))
				{
					IsTransitioning = false;
					_currentCts = null;
					_currentDoneSignal = null;
				}
				myCts.Dispose();
			}
		}

		// ===========================================================================
		// 各操作のコア（ロールバック可能ゾーン / 完走必須ゾーンを意識）
		// ===========================================================================

		async UniTask PushCore(IScreenIdentifier id, PushOptions opt, UniTaskCompletionSource<IScreenDataReader> resultSource, CancellationToken ct)
		{
			var from = Current;
			FireStart(from, id, ScreenTransitionKind.Push);

			// --- ロールバック可能ゾーン ---
			LiveEntry entry;
			try
			{
				entry = await CreateAndPreloadAsync(id, opt.Data, ct);
				ct.ThrowIfCancellationRequested();
			}
			catch (OperationCanceledException)
			{
				// ロールバックゾーンでの cancel：PushAndAwait 呼び出し側にも伝播
				resultSource?.TrySetCanceled();
				throw;
			}
			entry.ResultSource = resultSource;

			// --- 完走必須ゾーン ---
			var safeCt = CancellationToken.None;
			try
			{
				var director = opt.TransitionDirector ?? _config.DefaultTransition;
				var transition = director?.CreateHandle();
				if (transition != null) await transition.Start(safeCt);

				entry.Modal = ResolveModal(opt.ModalOverride);

				if (_live.Count > 0)
				{
					if (_config.StackMode == StackMode.Cover)
					{
						var prev = _live[_live.Count - 1];
						var cache = ResolveCacheMode(_history[_history.Count - 1], opt.CachePolicyOverride);
						await ExitPreviousAsync(prev, cache, isPop: false, safeCt);
						if (cache == ScreenCacheMode.DestroyOnCover)
							_live[_live.Count - 1] = null;
					}
					// Stack mode: 前画面はそのまま残す（visible + active）
				}

				// Stack mode + Modal で入力遮蔽ブロッカーを挿入（new screen より下、前画面より上）
				if (ShouldCreateBlocker(entry.Modal) && _live.Count > 0)
				{
					entry.ModalBlocker = CreateModalBlocker(_config.Container.Root);
				}

				entry.View.SetParent(_config.Container.Root);
				entry.View.SetActive(true);
				await entry.Presenter.OnBeforeEnter(entry.PushPayload, safeCt);

				await RunEnterAsync(entry, transition, safeCt);

				await entry.Presenter.OnAfterEnter(EmptyScreenDataReader.Instance, safeCt);
				entry.PushPayload = null;

				_history.Push(id);
				_live.Add(entry);
			}
			finally
			{
				FireEnd(from, Current, ScreenTransitionKind.Push);
			}
		}

		async UniTask PopCore(PopOptions opt, CancellationToken ct)
		{
			if (_history.Count <= 1) return;
			var from = Current;
			FireStart(from, _history.Count >= 2 ? _history[_history.Count - 2] : null, ScreenTransitionKind.Pop);

			// Pop は最初から完走必須ゾーン
			var safeCt = CancellationToken.None;
			try
			{
				var director = opt.TransitionDirector ?? _config.DefaultTransition;
				var transition = director?.CreateHandle();
				if (transition != null) await transition.Start(safeCt);

				var top = _live[_live.Count - 1];
				var returnStore = new ScreenDataStore();
				await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: true, safeCt, returnStore, isNormalPop: true);
				DestroyBlockerIfAny(top);

				_live.RemoveAt(_live.Count - 1);
				_history.PopCurrent();

				var belowIndex = _live.Count - 1;
				var below = _live[belowIndex];
				// Enter アニメは「下が再表示される」場合だけ走らせる
				//  - Cover + Destroy → reload された：true
				//  - Cover + Keep → Suspend していたものを起こす：true
				//  - Stack → そもそも常時 visible だった：false
				bool belowReappears;
				if (below == null)
				{
					var belowId = _history[belowIndex];
					below = await CreateAndPreloadAsync(belowId, data: null, safeCt);
					below.View.SetParent(_config.Container.Root);
					below.View.SetActive(true);
					_live[belowIndex] = below;
					belowReappears = true;
				}
				else if (below.Suspended)
				{
					below.View.SetActive(true);
					await below.Presenter.OnResume(safeCt);
					below.Suspended = false;
					belowReappears = true;
				}
				else
				{
					below.View.SetActive(true); // 念のため（Stack なら既に true）
					belowReappears = false;
				}

				await below.Presenter.OnBeforeEnter(returnStore, safeCt);
				await RunEnterAsync(below, transition, safeCt, playViewEnter: belowReappears);
				await below.Presenter.OnAfterEnter(EmptyScreenDataReader.Instance, safeCt);
			}
			finally
			{
				FireEnd(from, Current, ScreenTransitionKind.Pop);
			}
		}

		async UniTask ReplaceCore(IScreenIdentifier id, ReplaceOptions opt, CancellationToken ct)
		{
			if (_history.Count == 0)
			{
				await PushCore(id, new PushOptions
				{
					Data = opt.Data,
					TransitionDirector = opt.TransitionDirector,
					CachePolicyOverride = opt.CachePolicyOverride,
					ModalOverride = opt.ModalOverride,
					InterruptPriority = opt.InterruptPriority,
				}, resultSource: null, ct);
				return;
			}

			var from = Current;
			FireStart(from, id, ScreenTransitionKind.Replace);

			// ロールバック可能ゾーン
			var newEntry = await CreateAndPreloadAsync(id, opt.Data, ct);
			ct.ThrowIfCancellationRequested();

			// 完走必須ゾーン
			var safeCt = CancellationToken.None;
			try
			{
				var director = opt.TransitionDirector ?? _config.DefaultTransition;
				var transition = director?.CreateHandle();
				if (transition != null) await transition.Start(safeCt);

				var top = _live[_live.Count - 1];
				await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: false, safeCt);
				DestroyBlockerIfAny(top);

				newEntry.Modal = ResolveModal(opt.ModalOverride);
				// Stack mode + Modal で blocker を新 entry に付ける（下に他画面が残っている場合のみ）
				if (ShouldCreateBlocker(newEntry.Modal) && _live.Count >= 2)
				{
					newEntry.ModalBlocker = CreateModalBlocker(_config.Container.Root);
				}

				_live[_live.Count - 1] = newEntry;
				_history.ReplaceCurrent(id);

				newEntry.View.SetParent(_config.Container.Root);
				newEntry.View.SetActive(true);
				await newEntry.Presenter.OnBeforeEnter(newEntry.PushPayload, safeCt);
				await RunEnterAsync(newEntry, transition, safeCt);
				await newEntry.Presenter.OnAfterEnter(EmptyScreenDataReader.Instance, safeCt);
				newEntry.PushPayload = null;
			}
			finally
			{
				FireEnd(from, Current, ScreenTransitionKind.Replace);
			}
		}

		// ===========================================================================
		// 内部ヘルパー
		// ===========================================================================

		async UniTask<LiveEntry> CreateAndPreloadAsync(IScreenIdentifier id, IScreenData data, CancellationToken ct)
		{
			var presenter = id.CreatePresenter(_services);
			var handle = id.CreateHandle(_services);
			var pushStore = new ScreenDataStore();
			if (data != null) pushStore.WriteUntyped(data);

			try
			{
				// 並列起動
				var loadTask = handle.Load(progress: null, ct);
				var preloadTask = presenter.OnBeforeLoad(pushStore, ct);
				await preloadTask;
				var view = await loadTask;

				view.SetActive(false);
				await presenter.OnAfterLoad(view, pushStore, ct);

				return new LiveEntry
				{
					Presenter = presenter,
					Handle = handle,
					View = view,
					PushPayload = pushStore,
					ResolvedCacheMode = ResolveCacheMode(id, cacheOverride: null),
				};
			}
			catch (OperationCanceledException)
			{
				try { await handle.Unload(CancellationToken.None); }
				catch { /* cleanup 中のエラーは握り潰す */ }
				throw;
			}
		}

		/// <summary>
		/// entry を退場させる。<paramref name="isNormalPop"/> が true なら
		/// PushAndAwait の awaiter に結果を return する（PopCore からの正規 Pop 用）。
		/// それ以外（DismissAll / Reset / Change / Push の Cover で押し出され / PopTo 中間 / Replace 上書き）は
		/// awaiter を TrySetCanceled し、OCE で抜けさせる。
		/// </summary>
		async UniTask ExitPreviousAsync(LiveEntry entry, ScreenCacheMode cacheMode, bool isPop, CancellationToken ct, ScreenDataStore returnStore = null, bool isNormalPop = false)
		{
			var store = returnStore ?? new ScreenDataStore();
			var writer = (IScreenDataWriter)store;
			await entry.Presenter.OnBeforeExit(writer, ct);
			var anim = entry.View.As<IScreenAnimatedView>();
			if (anim != null) await anim.PlayExit(ct);
			entry.View.SetActive(false);
			await entry.Presenter.OnAfterExit(writer, ct);

			if (cacheMode == ScreenCacheMode.DestroyOnCover || isPop)
			{
				await entry.Handle.Unload(ct);
				await entry.Presenter.OnAfterUnload(writer, ct);
				if (entry.ResultSource != null)
				{
					if (isNormalPop) entry.ResultSource.TrySetResult(store);
					else entry.ResultSource.TrySetCanceled();
					entry.ResultSource = null;
				}
			}
			else
			{
				// Cover + Keep: 寝かせる（StackMode は Cover のみここに来る）
				// ResultSource は未解決のまま：後で本当に Pop されるときに resolve
				await entry.Presenter.OnSuspend(ct);
				entry.Suspended = true;
			}
		}

		async UniTask ClearAllExceptCurrentAsync(CancellationToken ct)
		{
			for (var i = _history.Count - 2; i >= 0; i--)
			{
				if (_live[i] != null)
				{
					await ExitPreviousAsync(_live[i], ScreenCacheMode.DestroyOnCover, isPop: true, CancellationToken.None);
					DestroyBlockerIfAny(_live[i]);
				}
				_live.RemoveAt(i);
			}
			_history.Edit(e => e.Clear());
		}

		ScreenCacheMode ResolveCacheMode(IScreenIdentifier id, ScreenCacheMode? cacheOverride)
			=> cacheOverride ?? id.CachePolicy ?? _config.DefaultCacheMode;

		/// <summary>
		/// Enter フェーズの「transition.End」と「View 個別の PlayEnter」を並列で走らせる。
		/// どちらも null 安全。playViewEnter=false なら PlayEnter はスキップ（Stack pop の下層など）。
		/// </summary>
		async UniTask RunEnterAsync(LiveEntry entry, IScreenTransitionHandle transition, CancellationToken ct, bool playViewEnter = true)
		{
			var endTask = transition?.End(ct) ?? UniTask.CompletedTask;
			var anim = playViewEnter ? entry.View.As<IScreenAnimatedView>() : null;
			var enterTask = anim?.PlayEnter(ct) ?? UniTask.CompletedTask;
			await UniTask.WhenAll(endTask, enterTask);
		}

		bool ResolveModal(bool? modalOverride)
			=> modalOverride ?? _config.DefaultModal;

		bool ShouldCreateBlocker(bool effectiveModal)
			=> _config.StackMode == StackMode.Stack
			&& _config.StackInputPolicy == StackInputPolicy.BlockUnderlying
			&& effectiveModal;

		GameObject CreateModalBlocker(Transform parent)
		{
			if (parent == null) return null;
			var go = new GameObject("ScreenFramework.ModalBlocker",
				typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			var rt = (RectTransform)go.transform;
			rt.SetParent(parent, worldPositionStays: false);
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			rt.SetAsLastSibling();
			var img = go.GetComponent<Image>();
			img.color = new Color(0f, 0f, 0f, 0f); // 完全透明
			img.raycastTarget = true;              // 入力を吸う
			return go;
		}

		void DestroyBlockerIfAny(LiveEntry entry)
		{
			if (entry?.ModalBlocker == null) return;
			if (Application.isPlaying) UnityEngine.Object.Destroy(entry.ModalBlocker);
			else UnityEngine.Object.DestroyImmediate(entry.ModalBlocker);
			entry.ModalBlocker = null;
		}

		void FireStart(IScreenIdentifier from, IScreenIdentifier to, ScreenTransitionKind kind)
			=> OnTransitionStart?.Invoke(new ScreenTransitionEvent(from, to, kind));

		void FireEnd(IScreenIdentifier from, IScreenIdentifier to, ScreenTransitionKind kind)
			=> OnTransitionEnd?.Invoke(new ScreenTransitionEvent(from, to, kind));

		sealed class LiveEntry
		{
			public IScreenPresenter Presenter;
			public IScreenHandle Handle;
			public IScreenViewInstance View;
			public ScreenCacheMode ResolvedCacheMode;
			public bool Modal;
			public bool Suspended;
			public GameObject ModalBlocker;
			public ScreenDataStore PushPayload;
			public UniTaskCompletionSource<IScreenDataReader> ResultSource;
		}
	}
}
