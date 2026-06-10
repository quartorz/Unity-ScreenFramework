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
							await ExitPreviousAsync(_live[i], ScreenCacheMode.DestroyOnCover, isPop: true, effect: null, CancellationToken.None);
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
					await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: true, effect: null, CancellationToken.None);
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
						await ExitPreviousAsync(prev, cache, isPop: false, effect, safeCt);
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
					entry.Presenter.OnBeforeEnter(entry.PushPayload, safeCt),
					effect?.OnBeforeEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);

				await RunEnterAsync(entry, effect, safeCt);

				await WhenBoth(
					entry.Presenter.OnAfterEnter(EmptyNavigationDataReader.Instance, safeCt),
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
				await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: true, effect, safeCt, returnStore, isNormalPop: true);
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
					below.Presenter.OnBeforeEnter(returnStore, safeCt),
					effect?.OnBeforeEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
				await RunEnterAsync(below, effect, safeCt, playViewEnter: belowReappears);
				await WhenBoth(
					below.Presenter.OnAfterEnter(EmptyNavigationDataReader.Instance, safeCt),
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
			await ExitPreviousAsync(entry, ScreenCacheMode.DestroyOnCover, isPop: true, effect: null, safeCt, returnStore, isNormalPop: true);
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
				await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: true, effect, safeCt, returnStore, isNormalPop: true);
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
						below.Presenter.OnBeforeEnter(returnStore, safeCt),
						effect?.OnBeforeEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
					await RunEnterAsync(below, effect, safeCt, playViewEnter: belowReappears);
					await WhenBoth(
						below.Presenter.OnAfterEnter(EmptyNavigationDataReader.Instance, safeCt),
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
				ct.ThrowIfCancellationRequested();

				// 完走必須ゾーン
				var safeCt = CancellationToken.None;
				var top = _live[_live.Count - 1];
				await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: false, effect, safeCt);
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
					newEntry.Presenter.OnBeforeEnter(newEntry.PushPayload, safeCt),
					effect?.OnBeforeEnter(EffectZone.Commit, safeCt) ?? UniTask.CompletedTask);
				await RunEnterAsync(newEntry, effect, safeCt);
				await WhenBoth(
					newEntry.Presenter.OnAfterEnter(EmptyNavigationDataReader.Instance, safeCt),
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
			var handle = id.CreateHandle(_services);

			try
			{
				// 並列起動: Presenter.OnBeforeLoad / handle.Load / Effect.OnBeforeLoad
				var loadTask = handle.Load(progress: null, ct);
				var preloadTask = presenter.OnBeforeLoad(pushStore, ct);
				var effectBeforeLoad = effect?.OnBeforeLoad(loadZone, ct) ?? UniTask.CompletedTask;
				await UniTask.WhenAll(preloadTask, effectBeforeLoad);
				var view = await loadTask;

				view.SetActive(false);
				await WhenBoth(
					presenter.OnAfterLoad(view, pushStore, ct),
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
		async UniTask ExitPreviousAsync(LiveEntry entry, ScreenCacheMode cacheMode, bool isPop, EffectRunner effect, CancellationToken ct, NavigationDataStore returnStore = null, bool isNormalPop = false)
		{
			var store = returnStore ?? new NavigationDataStore();
			var writer = (INavigationDataWriter)store;

			// Exit は常に完走必須ゾーン。
			await WhenBoth(
				entry.Presenter.OnBeforeExit(writer, ct),
				effect?.OnBeforeExit(EffectZone.Commit, ct) ?? UniTask.CompletedTask);

			var anim = entry.View.As<IScreenAnimatedView>();
			if (anim != null) await anim.PlayExit(ct);
			entry.View.SetActive(false);

			await WhenBoth(
				entry.Presenter.OnAfterExit(writer, ct),
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
					await ExitPreviousAsync(_live[i], ScreenCacheMode.DestroyOnCover, isPop: true, effect: null, CancellationToken.None);
					DestroyBlockerIfAny(_live[i]);
				}
				_live.RemoveAt(i);
			}
			_history.Edit(e => e.Clear());
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

		void FireStart(IScreenIdentifier from, IScreenIdentifier to, ScreenTransitionKind kind)
			=> OnTransitionStart?.Invoke(new ScreenTransitionEvent(from, to, kind));

		void FireEnd(IScreenIdentifier from, IScreenIdentifier to, ScreenTransitionKind kind)
			=> OnTransitionEnd?.Invoke(new ScreenTransitionEvent(from, to, kind));

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
	}
}
