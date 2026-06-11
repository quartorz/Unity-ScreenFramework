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

		// Preempt 用：FIFO チェーンの全 pending CTS と最新の完了シグナル。
		// UniTask は単一 await 設計のため、複数の後続が完了を観測できるよう
		// UniTaskCompletionSource を完了シグナルとして使う。
		// Preempt は自分より前の pending を全てキャンセルするためリストで保持する。
		readonly List<CancellationTokenSource> _pendingCtses = new();
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
			// History.Edit は _live と同期して編集する必要がある（履歴だけ書き換わると
			// 平行リストの不変条件が壊れ、以後の Pop が別画面を復元する）ため Navigator 側で実装する。
			_history.EditOverride = EditHistorySynced;
		}

		// ===========================================================================
		// 公開 API
		// ===========================================================================

		public async UniTask<IScreenEntry> Push(IScreenIdentifier id, PushOptions opt = default, CancellationToken ct = default)
		{
			if (id == null) throw new ArgumentNullException(nameof(id));
			LiveEntry created = null;
			await Run(opt.InterruptPriority, ct, async myCt =>
			{
				var from = Current;
				FireStart(from, id, ScreenTransitionKind.Push);
				try { created = await PushCore(OperationKind.Push, id, opt, resultSource: null, myCt); }
				finally { FireEnd(from, Current, ScreenTransitionKind.Push); }
			});
			return created != null ? new ScreenEntry(this, created.Presenter) : null;
		}

		public IScreenEntry FindEntry<TPresenter>() where TPresenter : class, IScreenPresenter
		{
			for (var i = _live.Count - 1; i >= 0; i--)
			{
				var e = _live[i];
				if (e != null && e.Presenter is TPresenter)
					return new ScreenEntry(this, e.Presenter);
			}
			return null;
		}

		public async UniTask<TResult> PushAndAwait<TResult>(ScreenIdentifier<TResult> id, PushOptions opt = default, CancellationToken ct = default)
			where TResult : INavigationData
		{
			if (id == null) throw new ArgumentNullException(nameof(id));

			var tcs = new UniTaskCompletionSource<INavigationDataReader>();

			await Run(opt.InterruptPriority, ct, async myCt =>
			{
				var from = Current;
				FireStart(from, id, ScreenTransitionKind.Push);
				try { await PushCore(OperationKind.Push, id, opt, resultSource: tcs, myCt); }
				finally { FireEnd(from, Current, ScreenTransitionKind.Push); }
			});

			var reader = await tcs.Task;
			return reader.TryRead<TResult>(out var result) ? result : default;
		}

		public UniTask Pop(PopOptions opt = default, CancellationToken ct = default)
		{
			return Run(opt.InterruptPriority, ct, async myCt =>
			{
				if (_history.Count <= 1) return;
				var from = Current;
				var to = _history.Count >= 2 ? _history[_history.Count - 2] : null;
				FireStart(from, to, ScreenTransitionKind.Pop);
				try { await PopCore(OperationKind.Pop, opt.Configure, myCt); }
				finally { FireEnd(from, Current, ScreenTransitionKind.Pop); }
			});
		}

		public UniTask Close(IScreenPresenter target, PopOptions opt = default, CancellationToken ct = default)
		{
			if (target == null) throw new ArgumentNullException(nameof(target));
			if (!Owns(target)) return UniTask.CompletedTask;
			return Run(opt.InterruptPriority, ct, async myCt =>
			{
				var idx = -1;
				for (var i = 0; i < _live.Count; i++)
				{
					if (_live[i] != null && ReferenceEquals(_live[i].Presenter, target)) { idx = i; break; }
				}
				if (idx < 0) return;

				if (idx == _live.Count - 1)
				{
					var from = Current;
					var to = _history.Count >= 2 ? _history[_history.Count - 2] : null;
					FireStart(from, to, ScreenTransitionKind.Pop);
					try { await CloseTopAsync(opt.Configure, myCt); }
					finally { FireEnd(from, Current, ScreenTransitionKind.Pop); }
				}
				else
				{
					await CloseMiddleAsync(idx, myCt);
				}
			});
		}

		bool Owns(IScreenPresenter target)
		{
			for (var i = 0; i < _live.Count; i++)
			{
				if (_live[i] != null && ReferenceEquals(_live[i].Presenter, target)) return true;
			}
			return false;
		}

		public UniTask Replace(IScreenIdentifier id, ReplaceOptions opt = default, CancellationToken ct = default)
		{
			if (id == null) throw new ArgumentNullException(nameof(id));
			return Run(opt.InterruptPriority, ct, async myCt =>
			{
				var from = Current;
				FireStart(from, id, ScreenTransitionKind.Replace);
				try { await ReplaceCore(OperationKind.Replace, id, opt, myCt); }
				finally { FireEnd(from, Current, ScreenTransitionKind.Replace); }
			});
		}

		public UniTask Change(IScreenIdentifier id, ChangeOptions opt = default, CancellationToken ct = default)
		{
			if (id == null) throw new ArgumentNullException(nameof(id));
			return Run(opt.InterruptPriority, ct, async myCt =>
			{
				var from = Current;
				FireStart(from, id, ScreenTransitionKind.Change);
				try
				{
					await ClearAllExceptCurrentAsync(myCt);
					await ReplaceCore(OperationKind.Change, id, new ReplaceOptions
					{
						Configure = opt.Configure,
						CachePolicyOverride = opt.CachePolicyOverride,
						ModalOverride = opt.ModalOverride,
					}, myCt);
				}
				finally { FireEnd(from, Current, ScreenTransitionKind.Change); }
			});
		}

		public UniTask Reset(IScreenIdentifier id, ResetOptions opt = default, CancellationToken ct = default)
		{
			if (id == null) throw new ArgumentNullException(nameof(id));
			return Run(opt.InterruptPriority, ct, async myCt =>
			{
				var from = Current;
				FireStart(from, id, ScreenTransitionKind.Reset);
				try
				{
					await DismissAllInternal(myCt);
					await PushCore(OperationKind.Reset, id, new PushOptions
					{
						Configure = opt.Configure,
						CachePolicyOverride = opt.CachePolicyOverride,
						ModalOverride = opt.ModalOverride,
					}, resultSource: null, myCt);
				}
				finally { FireEnd(from, Current, ScreenTransitionKind.Reset); }
			});
		}

		public UniTask PopTo(Func<IScreenIdentifier, bool> predicate, PopToOptions opt = default, CancellationToken ct = default)
		{
			if (predicate == null) throw new ArgumentNullException(nameof(predicate));
			return Run(opt.InterruptPriority, ct, async myCt =>
			{
				var targetIndex = -1;
				for (var i = _history.Count - 1; i >= 0; i--)
				{
					if (predicate(_history[i])) { targetIndex = i; break; }
				}
				if (targetIndex < 0 || targetIndex == _history.Count - 1) return;

				var from = Current;
				var to = _history[targetIndex];
				FireStart(from, to, ScreenTransitionKind.PopTo);
				try
				{
					for (var i = _history.Count - 2; i > targetIndex; i--)
					{
						if (_live[i] != null)
						{
							await ExitPreviousAsync(_live[i], ScreenCacheMode.DestroyOnCover, isPop: true, effect: null, BareContext(OperationKind.PopTo, _history[i]), CancellationToken.None);
							DestroyBlockerIfAny(_live[i]);
						}
						_live.RemoveAt(i);
						_history.RemoveAtInternal(i);
					}

					await PopCore(OperationKind.PopTo, opt.Configure, myCt);
				}
				finally { FireEnd(from, Current, ScreenTransitionKind.PopTo); }
			});
		}

		public UniTask DismissAll(CancellationToken ct = default)
		{
			return Run(InterruptPriority.Preempt, ct, async myCt =>
			{
				await DismissAllInternal(myCt);
			});
		}

		async UniTask DismissAllInternal(CancellationToken ct)
		{
			while (_history.Count > 0)
			{
				var top = _live[_live.Count - 1];
				if (top != null)
				{
					await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: true, effect: null, BareContext(OperationKind.Reset, _history[_history.Count - 1]), CancellationToken.None);
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
			var prevDone = _currentDoneSignal;
			var myCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
			var myDone = new UniTaskCompletionSource();
			_pendingCtses.Add(myCts);
			_currentDoneSignal = myDone;
			IsTransitioning = true;

			if (priority == InterruptPriority.Preempt)
			{
				for (var i = _pendingCtses.Count - 2; i >= 0; i--)
				{
					_pendingCtses[i].Cancel();
				}
			}
			if (prevDone != null)
			{
				try { await prevDone.Task; }
				catch (OperationCanceledException) { /* preempt されただけ */ }
				catch { /* 前遷移のエラーは握り潰す */ }
			}

			try
			{
				myCts.Token.ThrowIfCancellationRequested();
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
				_pendingCtses.Remove(myCts);
				if (ReferenceEquals(_currentDoneSignal, myDone))
				{
					IsTransitioning = false;
					_currentDoneSignal = null;
				}
				myCts.Dispose();
			}
		}

		// ===========================================================================
		// Effect 解決
		// ===========================================================================

		/// <summary>
		/// 遷移用の TransitionContext を作る。configure は呼び出し側が bag に書き込むコールバック。
		/// Pop 系は from=top, to=below で呼ばれる。
		/// </summary>
		TransitionContext NewContext(OperationKind kind, IScreenIdentifier from, IScreenIdentifier to, Action<INavigationDataWriter> configure, out NavigationDataStore store)
		{
			store = new NavigationDataStore();
			configure?.Invoke(store);
			return new TransitionContext(kind, from, to, store, store);
		}

		/// <summary>
		/// Effect が絡まない silent な退場（Reset/PopTo/Change/DismissAll/Close-middle で吹き飛ぶ中間画面）用の
		/// 最小 ctx。stage を publish する相手の Effect はいないが、Presenter hook が常に非 null の
		/// ITransitionContext を受け取れるようにするためのもの（PublishStage は無害な no-op 相当になる）。
		/// </summary>
		ITransitionContext BareContext(OperationKind kind, IScreenIdentifier from)
			=> NewContext(kind, from, to: null, configure: null, out _);

		/// <summary>
		/// Registry が無い・マッチ無し・例外時は null を返す。EffectRunner は内部で全例外を吸収するため、
		/// 返ってきた runner はそのまま hook 経由で扱って良い。
		/// </summary>
		async UniTask<EffectRunner> ResolveAndInstantiateEffectAsync(ITransitionContext ctx, CancellationToken ct)
		{
			if (_config.Registry == null) return null;
			EffectRegistry.ResolveResult resolved;
			try
			{
				resolved = _config.Registry.Resolve(ctx.From, ctx.To, ctx);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return null;
			}
			if (!resolved.HasMatch) return null;
			if (_config.EffectRoot == null)
			{
				Debug.LogWarning("[ScreenFramework] EffectRegistry matched but EffectRoot is null. Skipping effect.");
				return null;
			}
			var runner = new EffectRunner(resolved.EffectPrefab, _config.EffectRoot, ctx);
			await runner.LoadAndInstantiateAsync(ct);
			return runner;
		}

		// ===========================================================================
		// 各操作のコア
		// ===========================================================================

		async UniTask<LiveEntry> PushCore(OperationKind kind, IScreenIdentifier id, PushOptions opt, UniTaskCompletionSource<INavigationDataReader> resultSource, CancellationToken ct)
		{
			var from = Current;
			var ctx = NewContext(kind, from, id, opt.Configure, out var pushStore);

			EffectRunner effect = null;
			try
			{
				// --- ロールバック可能ゾーン ---
				LiveEntry entry;
				try
				{
					effect = await ResolveAndInstantiateEffectAsync(ctx, ct);
					entry = await CreateAndPreloadAsync(id, pushStore, ctx, effect, EffectZone.Rollback, ct);
					if (ct.IsCancellationRequested)
					{
						// hook 側が ct を観測せず完走した場合でも、ロード済み entry を漏らさず巻き戻す
						await DiscardEntryAsync(entry);
					}
					ct.ThrowIfCancellationRequested();
				}
				catch (OperationCanceledException)
				{
					resultSource?.TrySetCanceled();
					throw;
				}
				entry.ResultSource = resultSource;

				// --- 完走必須ゾーン ---
				var safeCt = CancellationToken.None;
				entry.Modal = ResolveModal(opt.ModalOverride);

				if (_live.Count > 0)
				{
					if (_config.StackMode == StackMode.Cover)
					{
						var prev = _live[_live.Count - 1];
						var cache = ResolveCacheMode(_history[_history.Count - 1], opt.CachePolicyOverride);
						await ExitPreviousAsync(prev, cache, isPop: false, effect, ctx, safeCt);
						if (cache == ScreenCacheMode.DestroyOnCover)
							_live[_live.Count - 1] = null;
					}
					else
					{
						// Stack mode: 前画面はそのまま残す。Effect 側だけ Exit hook を進める。
						if (effect != null)
						{
							await effect.OnBeforeExit(EffectZone.Commit, safeCt);
							await effect.OnAfterExit(EffectZone.Commit, safeCt);
						}
					}
				}
				else
				{
					// 最初の Push: prev exit 無し。Effect 側だけ Exit hook を進める。
					if (effect != null)
					{
						await effect.OnBeforeExit(EffectZone.Commit, safeCt);
						await effect.OnAfterExit(EffectZone.Commit, safeCt);
					}
				}

				if (ShouldCreateBlocker(entry.Modal) && _live.Count > 0)
				{
					entry.ModalBlocker = CreateModalBlocker(_config.Container.Root);
				}

				entry.View.SetParent(_config.Container.Root);
				entry.View.SetActive(true);

				await WhenBoth(
					entry.Presenter.OnBeforeEnter(entry.PushPayload, ctx, safeCt),
					effect?.OnBeforeEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);

				await RunEnterAsync(entry, effect, safeCt);

				await WhenBoth(
					entry.Presenter.OnAfterEnter(EmptyNavigationDataReader.Instance, ctx, safeCt),
					effect?.OnAfterEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
				entry.PushPayload = null;

				_history.Push(id);
				_live.Add(entry);
				return entry;
			}
			finally
			{
				effect?.Finish();
			}
		}

		async UniTask PopCore(OperationKind kind, Action<INavigationDataWriter> configure, CancellationToken ct)
		{
			if (_history.Count <= 1) return;

			var from = Current;
			var toId = _history[_history.Count - 2];
			var ctx = NewContext(kind, from, toId, configure, out _);

			// Pop は最初から完走必須ゾーン
			var safeCt = CancellationToken.None;

			var effect = await ResolveAndInstantiateEffectAsync(ctx, safeCt);
			try
			{
				var top = _live[_live.Count - 1];
				var returnStore = new NavigationDataStore();
				await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: true, effect, ctx, safeCt, returnStore, isNormalPop: true);
				DestroyBlockerIfAny(top);

				_live.RemoveAt(_live.Count - 1);
				_history.PopCurrent();

				var belowIndex = _live.Count - 1;
				var below = _live[belowIndex];
				bool belowReappears;
				if (below == null)
				{
					var belowId = _history[belowIndex];
					// 復元 load は Exit より後 = 完走必須ゾーン。Load hook も Commit で呼ぶ。
					below = await CreateAndPreloadAsync(belowId, pushStore: new NavigationDataStore(), ctx, effect, EffectZone.Commit, safeCt);
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
					below.View.SetActive(true);
					belowReappears = false;
				}

				await WhenBoth(
					below.Presenter.OnBeforeEnter(returnStore, ctx, safeCt),
					effect?.OnBeforeEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
				await RunEnterAsync(below, effect, safeCt, playViewEnter: belowReappears);
				await WhenBoth(
					below.Presenter.OnAfterEnter(EmptyNavigationDataReader.Instance, ctx, safeCt),
					effect?.OnAfterEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
			}
			finally
			{
				effect?.Finish();
			}
		}

		/// <summary>
		/// 中間エントリを黙って消す（Effect なし）。
		/// </summary>
		async UniTask CloseMiddleAsync(int idx, CancellationToken ct)
		{
			var entry = _live[idx];
			var safeCt = CancellationToken.None;
			var returnStore = new NavigationDataStore();
			var ctx = BareContext(OperationKind.Close, _history[idx]);
			await ExitPreviousAsync(entry, ScreenCacheMode.DestroyOnCover, isPop: true, effect: null, ctx, safeCt, returnStore, isNormalPop: true);
			DestroyBlockerIfAny(entry);
			_live.RemoveAt(idx);
			_history.RemoveAtInternal(idx);
		}

		async UniTask CloseTopAsync(Action<INavigationDataWriter> configure, CancellationToken ct)
		{
			if (_live.Count == 0) return;

			var from = Current;
			var toId = _history.Count >= 2 ? _history[_history.Count - 2] : null;
			var ctx = NewContext(OperationKind.Close, from, toId, configure, out _);

			var safeCt = CancellationToken.None;
			var effect = await ResolveAndInstantiateEffectAsync(ctx, safeCt);
			try
			{
				var top = _live[_live.Count - 1];
				var returnStore = new NavigationDataStore();
				await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: true, effect, ctx, safeCt, returnStore, isNormalPop: true);
				DestroyBlockerIfAny(top);
				_live.RemoveAt(_live.Count - 1);
				_history.PopCurrent();

				if (_live.Count > 0)
				{
					var belowIndex = _live.Count - 1;
					var below = _live[belowIndex];
					bool belowReappears;
					if (below == null)
					{
						var belowId = _history[belowIndex];
						// 復元 load は Exit より後 = 完走必須ゾーン。
						below = await CreateAndPreloadAsync(belowId, pushStore: new NavigationDataStore(), ctx, effect, EffectZone.Commit, safeCt);
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
						below.View.SetActive(true);
						belowReappears = false;
					}

					await WhenBoth(
						below.Presenter.OnBeforeEnter(returnStore, ctx, safeCt),
						effect?.OnBeforeEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
					await RunEnterAsync(below, effect, safeCt, playViewEnter: belowReappears);
					await WhenBoth(
						below.Presenter.OnAfterEnter(EmptyNavigationDataReader.Instance, ctx, safeCt),
						effect?.OnAfterEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
				}
				else if (effect != null)
				{
					// 下が無い：Effect の Enter hook だけ完走させる
					await effect.OnBeforeEnter(EffectZone.Commit, safeCt);
					await effect.OnAfterEnter(EffectZone.Commit, safeCt);
				}
			}
			finally
			{
				effect?.Finish();
			}
		}

		async UniTask ReplaceCore(OperationKind kind, IScreenIdentifier id, ReplaceOptions opt, CancellationToken ct)
		{
			if (_history.Count == 0)
			{
				await PushCore(kind, id, new PushOptions
				{
					Configure = opt.Configure,
					CachePolicyOverride = opt.CachePolicyOverride,
					ModalOverride = opt.ModalOverride,
				}, resultSource: null, ct);
				return;
			}

			var from = Current;
			var ctx = NewContext(kind, from, id, opt.Configure, out var pushStore);

			EffectRunner effect = null;
			try
			{
				// ロールバック可能ゾーン
				effect = await ResolveAndInstantiateEffectAsync(ctx, ct);
				var newEntry = await CreateAndPreloadAsync(id, pushStore, ctx, effect, EffectZone.Rollback, ct);
				if (ct.IsCancellationRequested)
				{
					// hook 側が ct を観測せず完走した場合でも、ロード済み entry を漏らさず巻き戻す
					await DiscardEntryAsync(newEntry);
				}
				ct.ThrowIfCancellationRequested();

				// 完走必須ゾーン
				var safeCt = CancellationToken.None;
				var top = _live[_live.Count - 1];
				await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: false, effect, ctx, safeCt);
				DestroyBlockerIfAny(top);

				newEntry.Modal = ResolveModal(opt.ModalOverride);
				if (ShouldCreateBlocker(newEntry.Modal) && _live.Count >= 2)
				{
					newEntry.ModalBlocker = CreateModalBlocker(_config.Container.Root);
				}

				_live[_live.Count - 1] = newEntry;
				_history.ReplaceCurrent(id);

				newEntry.View.SetParent(_config.Container.Root);
				newEntry.View.SetActive(true);

				await WhenBoth(
					newEntry.Presenter.OnBeforeEnter(newEntry.PushPayload, ctx, safeCt),
					effect?.OnBeforeEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
				await RunEnterAsync(newEntry, effect, safeCt);
				await WhenBoth(
					newEntry.Presenter.OnAfterEnter(EmptyNavigationDataReader.Instance, ctx, safeCt),
					effect?.OnAfterEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
				newEntry.PushPayload = null;
			}
			finally
			{
				effect?.Finish();
			}
		}

		// ===========================================================================
		// 内部ヘルパー
		// ===========================================================================

		/// <summary>
		/// プレハブ Load + presenter.OnBeforeLoad/OnAfterLoad を並列で走らせる。
		/// effect が non-null なら effect の同名 hook も並列で実行する（個別 try/catch は EffectRunner 側）。
		/// <paramref name="loadZone"/> は Effect の Load hook の例外時挙動を決める。Push/Replace の新規 load は
		/// <see cref="EffectZone.Rollback"/>、Pop/Close の復元 load は Exit 後なので <see cref="EffectZone.Commit"/>。
		/// </summary>
		async UniTask<LiveEntry> CreateAndPreloadAsync(IScreenIdentifier id, NavigationDataStore pushStore, ITransitionContext ctx, EffectRunner effect, EffectZone loadZone, CancellationToken ct)
		{
			var presenter = id.CreatePresenter(_services);
			presenter.AssignServices(_services);
			// インスタンス組み立て hook。まだ何も load していないので例外時の cleanup 不要（そのまま伝播）
			await presenter.OnInitialize(ct);
			var handle = id.CreateHandle(_services);

			var loadStarted = false;
			UniTask<IScreenViewInstance> loadTask = default;
			try
			{
				// 並列起動: Presenter.OnBeforeLoad / handle.Load / Effect.OnBeforeLoad。
				// Preserve は preload 側が先に失敗した場合に catch 側で load の決着を待ち直すため
				// （UniTask は通常 1 回しか await できない）。
				loadTask = handle.Load(progress: null, ct).Preserve();
				loadStarted = true;
				var preloadTask = presenter.OnBeforeLoad(pushStore, ctx, ct);
				var effectBeforeLoad = effect?.OnBeforeLoad(loadZone, ct) ?? UniTask.CompletedTask;
				await UniTask.WhenAll(preloadTask, effectBeforeLoad);
				var view = await loadTask;

				view.SetActive(false);
				await WhenBoth(
					presenter.OnAfterLoad(view, pushStore, ctx, ct),
					effect?.OnAfterLoad(loadZone, ct) ?? UniTask.CompletedTask);

				return new LiveEntry
				{
					Presenter = presenter,
					Handle = handle,
					View = view,
					PushPayload = pushStore,
					ResolvedCacheMode = ResolveCacheMode(id, cacheOverride: null),
				};
			}
			catch
			{
				// 走行中の load を放置したまま Unload すると競合する（ロード完了後に
				// インスタンスが設定されて解放漏れになり得る）ので、先に load の決着を待つ。
				if (loadStarted)
				{
					try { await loadTask; }
					catch { /* cleanup */ }
				}
				try { await handle.Unload(CancellationToken.None); }
				catch { /* cleanup */ }
				try { await presenter.OnAfterUnload(pushStore, CancellationToken.None); }
				catch { /* cleanup */ }
				throw;
			}
		}

		/// <summary>
		/// entry を退場させる。effect が non-null なら effect の OnBeforeExit / OnAfterExit を並列で走らせる。
		/// </summary>
		async UniTask ExitPreviousAsync(LiveEntry entry, ScreenCacheMode cacheMode, bool isPop, EffectRunner effect, ITransitionContext ctx, CancellationToken ct, NavigationDataStore returnStore = null, bool isNormalPop = false)
		{
			var store = returnStore ?? new NavigationDataStore();
			var writer = (INavigationDataWriter)store;

			// Exit は常に完走必須ゾーン。
			await WhenBoth(
				entry.Presenter.OnBeforeExit(writer, ctx, ct),
				effect?.OnBeforeExit(EffectZone.Commit, ct) ?? UniTask.CompletedTask);

			var anim = entry.View.As<IScreenAnimatedView>();
			if (anim != null) await anim.PlayExit(ct);
			entry.View.SetActive(false);

			await WhenBoth(
				entry.Presenter.OnAfterExit(writer, ctx, ct),
				effect?.OnAfterExit(EffectZone.Commit, ct) ?? UniTask.CompletedTask);

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
					await ExitPreviousAsync(_live[i], ScreenCacheMode.DestroyOnCover, isPop: true, effect: null, BareContext(OperationKind.Change, _history[i]), CancellationToken.None);
					DestroyBlockerIfAny(_live[i]);
				}
				_live.RemoveAt(i);
			}
			// _live はこのループで同期済みなので、同期編集（EditHistorySynced）を介さず直接消す
			_history.ClearBelow();
		}

		/// <summary>
		/// <see cref="IScreenHistory.Edit"/> の実体。履歴と _live を同じ形に保ったまま編集する。
		/// 挿入行は dormant（LiveEntry null）として入り、削除・差し替えで履歴から外れた行に
		/// 生きたインスタンスがあれば Exit 演出・Exit hook なしで即 Unload する
		/// （Edit は同期 API のため、Unload と OnAfterUnload は投げっぱなしで行う）。
		/// 履歴が空のときは従来どおり編集を適用しない（Current が無い状態で行だけ増やすと
		/// top が dormant になり遷移操作の前提が壊れるため）。
		/// </summary>
		void EditHistorySynced(Action<IScreenHistoryEditor> action)
		{
			if (_history.Count == 0)
			{
				action(new SyncedHistoryEditor());
				return;
			}

			var editor = new SyncedHistoryEditor();
			for (var i = 0; i < _history.Count - 1; i++)
			{
				editor.Ids.Add(_history[i]);
				editor.Lives.Add(_live[i]);
			}

			action(editor);

			var currentLive = _live[_live.Count - 1];
			_history.RebuildBelow(editor.Ids);
			_live.Clear();
			_live.AddRange(editor.Lives);
			_live.Add(currentLive);

			foreach (var removed in editor.Removed)
			{
				CleanupDetachedEntry(removed);
			}
		}

		/// <summary>
		/// History.Edit で履歴から外された生き残り entry の後始末。
		/// Exit 演出・Exit hook は呼ばず、blocker 破棄と awaiter キャンセルだけ同期で済ませて
		/// Unload / OnAfterUnload を投げっぱなしで行う。
		/// </summary>
		void CleanupDetachedEntry(LiveEntry entry)
		{
			DestroyBlockerIfAny(entry);
			entry.ResultSource?.TrySetCanceled();
			entry.ResultSource = null;
			DiscardEntryAsync(entry).Forget();
		}

		/// <summary>
		/// ライフサイクルに乗らないまま手放す entry を Unload し、OnAfterUnload で
		/// 画面側に購読補償の機会を与える。後始末中のエラーはログに留める。
		/// </summary>
		static async UniTask DiscardEntryAsync(LiveEntry entry)
		{
			try { await entry.Handle.Unload(CancellationToken.None); }
			catch (Exception e) { Debug.LogException(e); }
			try { await entry.Presenter.OnAfterUnload(entry.PushPayload ?? new NavigationDataStore(), CancellationToken.None); }
			catch (Exception e) { Debug.LogException(e); }
		}

		ScreenCacheMode ResolveCacheMode(IScreenIdentifier id, ScreenCacheMode? cacheOverride)
			=> cacheOverride ?? id.CachePolicy ?? _config.DefaultCacheMode;

		/// <summary>
		/// Enter フェーズ: View 個別の PlayEnter を待つ（旧 transition.End はもう存在しない）。
		/// </summary>
		async UniTask RunEnterAsync(LiveEntry entry, EffectRunner effect, CancellationToken ct, bool playViewEnter = true)
		{
			var anim = playViewEnter ? entry.View.As<IScreenAnimatedView>() : null;
			if (anim != null) await anim.PlayEnter(ct);
		}

		/// <summary>
		/// 2 つの UniTask を並列で待つ薄いヘルパ。Effect 側は EffectRunner 内部で例外吸収済みなので
		/// WhenAll で待っても Presenter 側を巻き込まない。
		/// </summary>
		static UniTask WhenBoth(UniTask a, UniTask b) => UniTask.WhenAll(a, b);

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
			img.color = new Color(0f, 0f, 0f, 0f);
			img.raycastTarget = true;
			return go;
		}

		void DestroyBlockerIfAny(LiveEntry entry)
		{
			if (entry?.ModalBlocker == null) return;
			if (Application.isPlaying) UnityEngine.Object.Destroy(entry.ModalBlocker);
			else UnityEngine.Object.DestroyImmediate(entry.ModalBlocker);
			entry.ModalBlocker = null;
		}

		// 観測側（アナリティクス等）の例外で遷移本筋を殺さない。
		// 特に FireEnd は finally から呼ばれるため、素通しすると元の例外を握り潰してしまう。
		void FireStart(IScreenIdentifier from, IScreenIdentifier to, ScreenTransitionKind kind)
		{
			try { OnTransitionStart?.Invoke(new ScreenTransitionEvent(from, to, kind)); }
			catch (Exception e) { Debug.LogException(e); }
		}

		void FireEnd(IScreenIdentifier from, IScreenIdentifier to, ScreenTransitionKind kind)
		{
			try { OnTransitionEnd?.Invoke(new ScreenTransitionEvent(from, to, kind)); }
			catch (Exception e) { Debug.LogException(e); }
		}

		sealed class ScreenEntry : IScreenEntry
		{
			readonly ScreenNavigatorImpl _nav;
			public IScreenPresenter Presenter { get; }

			public ScreenEntry(ScreenNavigatorImpl nav, IScreenPresenter presenter)
			{
				_nav = nav;
				Presenter = presenter;
			}

			public bool IsAlive => _nav.Owns(Presenter);
			public UniTask Close(PopOptions opt = default, CancellationToken ct = default)
				=> _nav.Close(Presenter, opt, ct);
		}

		sealed class LiveEntry
		{
			public IScreenPresenter Presenter;
			public IScreenHandle Handle;
			public IScreenViewInstance View;
			public ScreenCacheMode ResolvedCacheMode;
			public bool Modal;
			public bool Suspended;
			public GameObject ModalBlocker;
			public NavigationDataStore PushPayload;
			public UniTaskCompletionSource<INavigationDataReader> ResultSource;
		}

		/// <summary>
		/// <see cref="EditHistorySynced"/> 用のエディタ。Identifier 列と LiveEntry 列を常に同じ形に保ち、
		/// 編集で外れた生き残り LiveEntry を <see cref="Removed"/> に集める。
		/// <see cref="IScreenHistoryEditor.Stack"/> 経由の素の IList 操作も全て同期される。
		/// </summary>
		sealed class SyncedHistoryEditor : IScreenHistoryEditor
		{
			public readonly List<IScreenIdentifier> Ids = new();
			public readonly List<LiveEntry> Lives = new();
			public readonly List<LiveEntry> Removed = new();

			StackView _stackView;
			public IList<IScreenIdentifier> Stack => _stackView ??= new StackView(this);

			public void Clear()
			{
				for (var i = Ids.Count - 1; i >= 0; i--) RemoveRowAt(i);
			}

			public void RemoveAt(int index) => RemoveRowAt(index);

			public void RemoveAll(Predicate<IScreenIdentifier> match)
			{
				for (var i = Ids.Count - 1; i >= 0; i--)
				{
					if (match(Ids[i])) RemoveRowAt(i);
				}
			}

			public void Insert(int index, IScreenIdentifier id) => InsertRowAt(index, id);

			void RemoveRowAt(int index)
			{
				if (Lives[index] != null) Removed.Add(Lives[index]);
				Ids.RemoveAt(index);
				Lives.RemoveAt(index);
			}

			void InsertRowAt(int index, IScreenIdentifier id)
			{
				Ids.Insert(index, id);
				Lives.Insert(index, null);
			}

			void ReplaceRowAt(int index, IScreenIdentifier id)
			{
				// Identifier の差し替えは別画面化なので、元の生き残りインスタンスは破棄対象に回す
				if (Lives[index] != null)
				{
					Removed.Add(Lives[index]);
					Lives[index] = null;
				}
				Ids[index] = id;
			}

			sealed class StackView : IList<IScreenIdentifier>
			{
				readonly SyncedHistoryEditor _editor;
				public StackView(SyncedHistoryEditor editor) { _editor = editor; }

				public IScreenIdentifier this[int index]
				{
					get => _editor.Ids[index];
					set => _editor.ReplaceRowAt(index, value);
				}

				public int Count => _editor.Ids.Count;
				public bool IsReadOnly => false;
				public void Add(IScreenIdentifier id) => _editor.InsertRowAt(_editor.Ids.Count, id);
				public void Clear() => _editor.Clear();
				public bool Contains(IScreenIdentifier id) => _editor.Ids.Contains(id);
				public void CopyTo(IScreenIdentifier[] array, int arrayIndex) => _editor.Ids.CopyTo(array, arrayIndex);
				public IEnumerator<IScreenIdentifier> GetEnumerator() => _editor.Ids.GetEnumerator();
				public int IndexOf(IScreenIdentifier id) => _editor.Ids.IndexOf(id);
				public void Insert(int index, IScreenIdentifier id) => _editor.InsertRowAt(index, id);

				public bool Remove(IScreenIdentifier id)
				{
					var index = _editor.Ids.IndexOf(id);
					if (index < 0) return false;
					_editor.RemoveRowAt(index);
					return true;
				}

				public void RemoveAt(int index) => _editor.RemoveRowAt(index);
				System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
			}
		}
	}
}
