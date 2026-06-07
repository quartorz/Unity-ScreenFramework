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
				try { created = await PushCore(id, opt, resultSource: null, myCt); }
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
			where TResult : IScreenData
		{
			if (id == null) throw new ArgumentNullException(nameof(id));

			// このエントリ専用の結果完了ソース。ExitPreviousAsync で TrySetResult される。
			var tcs = new UniTaskCompletionSource<IScreenDataReader>();

			// Push 自体は通常通り Run。tcs を PushCore に持ち込んで entry.ResultSource に貼る。
			await Run(opt.InterruptPriority, ct, async myCt =>
			{
				var from = Current;
				FireStart(from, id, ScreenTransitionKind.Push);
				try { await PushCore(id, opt, resultSource: tcs, myCt); }
				finally { FireEnd(from, Current, ScreenTransitionKind.Push); }
			});

			// 自分のエントリが閉じるのを待つ。preempt/DismissAll/Reset/Change で死んだら
			// TrySetCanceled されて OperationCanceledException で抜ける。
			var reader = await tcs.Task;
			return reader.TryRead<TResult>(out var result) ? result : default;
		}

		public UniTask Pop(PopOptions opt = default, CancellationToken ct = default)
		{
			return Run(opt.InterruptPriority, ct, async myCt =>
			{
				if (_history.Count <= 1) return; // PopCore と同じガード（Fire しないため）
				var from = Current;
				var to = _history.Count >= 2 ? _history[_history.Count - 2] : null;
				FireStart(from, to, ScreenTransitionKind.Pop);
				try { await PopCore(opt, myCt); }
				finally { FireEnd(from, Current, ScreenTransitionKind.Pop); }
			});
		}

		public UniTask Close(IScreenPresenter target, PopOptions opt = default, CancellationToken ct = default)
		{
			if (target == null) throw new ArgumentNullException(nameof(target));
			// 所有を Run の外でチェック。所有していなければ完全に no-op で抜ける
			// （他レイヤー経由の Close 呼び出しでこの navigator の進行中遷移を巻き込まないため）。
			if (!Owns(target)) return UniTask.CompletedTask;
			return Run(opt.InterruptPriority, ct, async myCt =>
			{
				// Run 内で再度 idx を確定（pending 解消中に位置が変わっている可能性）。
				// Fire は top close 時のみ Pop を 1 発、middle close は silent（既存仕様）。
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
					try { await CloseTopAsync(opt, myCt); }
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
				try { await ReplaceCore(id, opt, myCt); }
				finally { FireEnd(from, Current, ScreenTransitionKind.Replace); }
			});
		}

		public UniTask Change(IScreenIdentifier id, ChangeOptions opt = default, CancellationToken ct = default)
		{
			if (id == null) throw new ArgumentNullException(nameof(id));
			// 複合操作全体を 1 つの Run に閉じる。途中で in-flight の遷移が割り込んで
			// _history/_live を mutate するのを防ぐ。
			return Run(opt.InterruptPriority, ct, async myCt =>
			{
				var from = Current;
				FireStart(from, id, ScreenTransitionKind.Change);
				try
				{
					await ClearAllExceptCurrentAsync(myCt);
					await ReplaceCore(id, new ReplaceOptions
					{
						Data = opt.Data,
						TransitionDirector = opt.TransitionDirector,
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
					await PushCore(id, new PushOptions
					{
						Data = opt.Data,
						TransitionDirector = opt.TransitionDirector,
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
							await ExitPreviousAsync(_live[i], ScreenCacheMode.DestroyOnCover, isPop: true, CancellationToken.None);
							DestroyBlockerIfAny(_live[i]);
						}
						_live.RemoveAt(i);
						_history.RemoveAtInternal(i);
					}

					await PopCore(new PopOptions
					{
						TransitionDirector = opt.TransitionDirector,
					}, myCt);
				}
				finally { FireEnd(from, Current, ScreenTransitionKind.PopTo); }
			});
		}

		public UniTask DismissAll(CancellationToken ct = default)
		{
			// DismissAll は専用 Kind を持たないため Fire しない（既存仕様維持）。
			// 必要なら ScreenTransitionKind に DismissAll を追加する。
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
			// await の「前」に自分をインストールする。こうしないと A 実行中に B, C が連続到着したとき、
			// B も C も同じ prevDone (=signalA) をキャプチャして待ち、A 完走で両方同時に resume → body 並走になる。
			// 先に自己インストールしておけば C は signalB をキャプチャして FIFO で繋がる。
			var prevDone = _currentDoneSignal;
			var myCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
			var myDone = new UniTaskCompletionSource();
			_pendingCtses.Add(myCts);
			_currentDoneSignal = myDone;
			IsTransitioning = true;

			if (priority == InterruptPriority.Preempt)
			{
				// 自分以外の全 pending を「自分の直前から逆順に」キャンセルする。
				// 順方向だと最古 (ctsA) の Cancel が UniTask の同期 continuation を引き起こし、
				// 次に並んでいる B の Run が body 直前の ThrowIfCancellationRequested を通過した後に
				// ctsB の Cancel が回ってくる順序になり、B が Load を始めて永久待機する。
				// 逆順なら B が先にキャンセル状態になり、A の Cancel の同期再入で B が resume しても
				// ThrowIfCancellationRequested(ctsB) で確実に落ちる。
				for (var i = _pendingCtses.Count - 2; i >= 0; i--)
				{
					_pendingCtses[i].Cancel();
				}
			}
			// 直前の遷移の完走を待つ（完了シグナル）
			if (prevDone != null)
			{
				try { await prevDone.Task; }
				catch (OperationCanceledException) { /* preempt されただけ */ }
				catch { /* 前遷移のエラーは握り潰す（自分とは別件） */ }
			}

			try
			{
				// 待機中に後続から preempt されていた場合は body を呼ばずに抜ける
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
				// 自分が最新のままなら状態クリア（後続が積まれていたら触らない）
				if (ReferenceEquals(_currentDoneSignal, myDone))
				{
					IsTransitioning = false;
					_currentDoneSignal = null;
				}
				myCts.Dispose();
			}
		}

		// ===========================================================================
		// 各操作のコア（ロールバック可能ゾーン / 完走必須ゾーンを意識）
		// ===========================================================================

		async UniTask<LiveEntry> PushCore(IScreenIdentifier id, PushOptions opt, UniTaskCompletionSource<IScreenDataReader> resultSource, CancellationToken ct)
		{
			// FireStart/FireEnd は public ラッパー側で行う（caller intent の Kind で fire するため）。
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
			return entry;
		}

		async UniTask PopCore(PopOptions opt, CancellationToken ct)
		{
			if (_history.Count <= 1) return;

			// FireStart/FireEnd は public ラッパー側で行う。
			// Pop は最初から完走必須ゾーン
			var safeCt = CancellationToken.None;
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

		/// <summary>
		/// 中間エントリを黙って消す（transition なし、上の画面は触らない）。
		/// Fire は呼ばない。
		/// </summary>
		async UniTask CloseMiddleAsync(int idx, CancellationToken ct)
		{
			var entry = _live[idx];
			var safeCt = CancellationToken.None;
			var returnStore = new ScreenDataStore();
			await ExitPreviousAsync(entry, ScreenCacheMode.DestroyOnCover, isPop: true, safeCt, returnStore, isNormalPop: true);
			DestroyBlockerIfAny(entry);
			_live.RemoveAt(idx);
			_history.RemoveAtInternal(idx);
		}

		/// <summary>
		/// 現在の top を閉じる。下があれば Pop と同じ流れで Enter させる。
		/// 下がなくても閉じる（Pop と違ってガードなし）。
		/// </summary>
		async UniTask CloseTopAsync(PopOptions opt, CancellationToken ct)
		{
			if (_live.Count == 0) return;

			// FireStart/FireEnd は public ラッパー側（Close）で行う。
			var safeCt = CancellationToken.None;
			var director = opt.TransitionDirector ?? _config.DefaultTransition;
			var transition = director?.CreateHandle();
			if (transition != null) await transition.Start(safeCt);

			var top = _live[_live.Count - 1];
			var returnStore = new ScreenDataStore();
			await ExitPreviousAsync(top, ScreenCacheMode.DestroyOnCover, isPop: true, safeCt, returnStore, isNormalPop: true);
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
					below.View.SetActive(true);
					belowReappears = false;
				}

				await below.Presenter.OnBeforeEnter(returnStore, safeCt);
				await RunEnterAsync(below, transition, safeCt, playViewEnter: belowReappears);
				await below.Presenter.OnAfterEnter(EmptyScreenDataReader.Instance, safeCt);
			}
			else if (transition != null)
			{
				// 下が無い：transition だけ完走させる
				await transition.End(safeCt);
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
				}, resultSource: null, ct);
				return;
			}

			// FireStart/FireEnd は public ラッパー側で行う。
			// ロールバック可能ゾーン
			var newEntry = await CreateAndPreloadAsync(id, opt.Data, ct);
			ct.ThrowIfCancellationRequested();

			// 完走必須ゾーン
			var safeCt = CancellationToken.None;
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

		// ===========================================================================
		// 内部ヘルパー
		// ===========================================================================

		async UniTask<LiveEntry> CreateAndPreloadAsync(IScreenIdentifier id, IScreenData data, CancellationToken ct)
		{
			var presenter = id.CreatePresenter(_services);
			presenter.AssignServices(_services);
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
			catch
			{
				// OCE / 非 OCE どちらでも handle を解放してから元の例外で抜ける。
				// Load パイプライン（OnBeforeLoad / handle.Load / OnAfterLoad）の失敗を
				// 利用側に OCE 詰め替えさせないため、catch (Exception) で受ける。
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
			public ScreenDataStore PushPayload;
			public UniTaskCompletionSource<IScreenDataReader> ResultSource;
		}
	}
}
