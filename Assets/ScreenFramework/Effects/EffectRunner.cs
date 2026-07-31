using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ScreenFramework
{
	/// <summary>
	/// Effect hook を実行する際のゾーン。例外時の Destroy タイミングを決める。
	/// hook 名ではなく「遷移の実フェーズ」を呼び出し側が指定する。
	/// 同じ hook（例: OnBeforeLoad）でも操作種別によってゾーンが変わるため
	/// （Push の load は <see cref="Rollback"/>、Pop 復元時の load は <see cref="Commit"/>）。
	/// </summary>
	internal enum EffectZone
	{
		/// <summary>ロールバック可能ゾーン。例外で即 Destroy（巻き戻し中なので継ぎ目もない）。</summary>
		Rollback,
		/// <summary>完走必須ゾーン。例外でも Destroy は遷移完了まで遅延（継ぎ目隠しの責務があり得る）。</summary>
		Commit,
	}

	/// <summary>
	/// 遷移 1 回分の Effect ライフサイクルを Navigator から駆動するためのラッパ。
	/// Matcher 解決済みの prefab を Load → Instantiate → 6 hook を順次呼び出す責任を持つ。
	/// 全段で例外を吸収（ログ + 無効化）し、本筋の遷移を絶対に止めない。
	/// </summary>
	internal sealed class EffectRunner
	{
		readonly AssetReferenceGameObject _prefabRef;
		readonly IEffectHost _host;
		readonly Transform _stagingParent;
		readonly ITransitionContext _ctx;

		ScreenEffect _instance;
		GameObject _instanceGo;
		AsyncOperationHandle<GameObject> _handle;
		bool _disabled;          // 例外発生後の以降 hook skip 用
		bool _ownsHandle;        // InstantiateAsync で取った handle を ReleaseInstance で返す必要があるか
		int _leasedOrder;        // host から借りた Sorting Order
		bool _hasLease;          // _leasedOrder を返却すべきか（lease 済みかつ未返却）

		public bool IsAlive => _instance != null;

		public EffectRunner(AssetReferenceGameObject prefabRef, IEffectHost host, Transform stagingParent, ITransitionContext ctx)
		{
			_prefabRef = prefabRef;
			_host = host;
			_stagingParent = stagingParent;
			_ctx = ctx;
		}

		/// <summary>
		/// Effect prefab の Load + Instantiate + <see cref="ScreenEffect.OnInitialize"/> の await。
		/// 失敗時は Disabled 化して以降 no-op。
		/// OnInitialize はまだ何も表示していない段階なので、ゾーンによらず例外で即 Destroy する。
		/// </summary>
		public async UniTask LoadAndInstantiateAsync(CancellationToken ct)
		{
			try
			{
				// 非アクティブな staging 親の下で生成し、描画されないまま受け取る。
				_handle = _stagingParent != null
					? _prefabRef.InstantiateAsync(_stagingParent)
					: _prefabRef.InstantiateAsync(_host.Root);
				_ownsHandle = true;
				while (!_handle.IsDone)
				{
					await UniTask.Yield(PlayerLoopTiming.Update, ct);
				}
				if (_handle.Status != AsyncOperationStatus.Succeeded)
				{
					Disable($"Effect prefab InstantiateAsync failed: {_handle.OperationException?.Message}");
					return;
				}
				var go = _handle.Result;
				if (go == null)
				{
					Disable("Effect prefab Instantiate returned null");
					return;
				}
				_instanceGo = go;
				// 走り始めた時点で host から order を借り、Canvas に camera / Sorting を流し込む。
				// 返却は teardown の単一経路 DestroyNow で行う。
				_leasedOrder = _host.LeaseOrder();
				_hasLease = true;
				ApplyCanvasSorting(go, _leasedOrder);
				// 設定が済んでから本来の親（Effect オーバーレイ）へ移し、ここで初めて見せる。
				// これでチラつき無しに、正しい camera / order で描画が始まる。
				go.transform.SetParent(_host.Root, worldPositionStays: false);
				if (!go.activeSelf) go.SetActive(true);
				_instance = go.GetComponent<ScreenEffect>();
				if (_instance == null)
				{
					Debug.LogError($"[ScreenFramework] Effect prefab '{go.name}' has no ScreenEffect component. Disabled.");
					DestroyNow();
					return;
				}

				await _instance.OnInitialize(_ctx, ct);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				// preempt 等で polling を抜けた直後は InstantiateAsync handle が未完了のことがある。
				// 未完了ハンドルを Release するとインスタンスが取り残されうるので、決着を待ってから破棄する。
				await SettleHandleAsync();
				DestroyNow();
				throw;
			}
			catch (Exception e)
			{
				// ct 起因でない OCE (OnInitialize の誤実装等) もここに落ちる。
				// Pop/Close は ct=None で protective try の外から呼ぶため、漏らすと遷移本筋が死ぬ
				Debug.LogException(e);
				DestroyNow();
				_disabled = true;
			}
		}

		// hook 名ではなくゾーン引数で例外時の挙動を決める。Load 系も Pop 復元時は Commit で呼ばれる。
		public UniTask OnBeforeLoad(EffectZone zone, CancellationToken ct) => RunHook(zone, _instance != null ? _instance.OnBeforeLoad : null, ct);
		public UniTask OnAfterLoad(EffectZone zone, CancellationToken ct)  => RunHook(zone, _instance != null ? _instance.OnAfterLoad  : null, ct);
		public UniTask OnBeforeHide(EffectZone zone, CancellationToken ct) => RunHook(zone, _instance != null ? _instance.OnBeforeHide : null, ct);
		public UniTask OnAfterHide(EffectZone zone, CancellationToken ct)  => RunHook(zone, _instance != null ? _instance.OnAfterHide  : null, ct);
		public UniTask OnBeforeShow(EffectZone zone, CancellationToken ct)=> RunHook(zone, _instance != null ? _instance.OnBeforeShow: null, ct);
		public UniTask OnAfterShow(EffectZone zone, CancellationToken ct) => RunHook(zone, _instance != null ? _instance.OnAfterShow : null, ct);

		/// <summary>
		/// 遷移完了時の最終クリーンアップ。完走必須ゾーンで Destroy 遅延した分もここで消える。
		/// </summary>
		public void Finish()
		{
			DestroyNow();
		}

		UniTask RunHook(EffectZone zone, Func<ITransitionContext, CancellationToken, UniTask> hook, CancellationToken ct)
			=> zone == EffectZone.Rollback ? RunHookRollbackZone(hook, ct) : RunHookCommitZone(hook, ct);

		async UniTask RunHookRollbackZone(Func<ITransitionContext, CancellationToken, UniTask> hook, CancellationToken ct)
		{
			if (_disabled || hook == null) return;
			try
			{
				await hook(_ctx, ct);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				// preempt 等で巻き戻し中 → 即 Destroy で巻き戻し継ぎ目もない
				DestroyNow();
				_disabled = true;
				throw;
			}
			catch (Exception e)
			{
				// ct 起因でない OCE（hook の誤実装による偽 OCE）もここに落とす。
				// 装飾の失敗は遷移本筋を止めない契約なので、キャンセルに化けさせず吸収する。
				Debug.LogException(e);
				DestroyNow();
				_disabled = true;
			}
		}

		async UniTask RunHookCommitZone(Func<ITransitionContext, CancellationToken, UniTask> hook, CancellationToken ct)
		{
			if (_disabled || hook == null) return;
			try
			{
				await hook(_ctx, ct);
			}
			catch (Exception e)
			{
				// 完走必須ゾーン: 残 hook skip のみ。ここで DestroyNow を呼ばないことが「Destroy 遅延」の実体で、
				// 実際の破棄は遷移完了時の Finish に任せる（継ぎ目隠しの責務があり得るため）
				Debug.LogException(e);
				_disabled = true;
			}
		}

		/// <summary>
		/// Effect の root Canvas に host のカメラ / Sorting を流し込む。画面・modal blocker と同じく
		/// Effect prefab の Canvas は ScreenSpaceCamera 前提だがカメラはシーン依存で prefab に焼けないため、
		/// Instantiate 直後にここで注入する。order は host が採番した重複しない値。
		/// </summary>
		void ApplyCanvasSorting(GameObject go, int order)
		{
			var canvas = go.GetComponent<Canvas>();
			if (canvas == null)
			{
				Debug.LogWarning($"[ScreenFramework] Effect prefab '{go.name}' has no root Canvas. Camera/sorting not applied.");
				return;
			}
			if (canvas.renderMode != RenderMode.ScreenSpaceCamera) canvas.renderMode = RenderMode.ScreenSpaceCamera;
			if (canvas.worldCamera != _host.RenderCamera) canvas.worldCamera = _host.RenderCamera;
			if (canvas.sortingLayerID != _host.SortingLayerId) canvas.sortingLayerID = _host.SortingLayerId;
			if (canvas.sortingOrder != order) canvas.sortingOrder = order;
		}

		void Disable(string reason)
		{
			Debug.LogWarning($"[ScreenFramework] {reason}. Effect disabled, transition continues.");
			DestroyNow();
			_disabled = true;
		}

		/// <summary>
		/// 巻き戻し中に InstantiateAsync handle が未完了なら決着を待つ。クリーンアップなので ct では中断しない。
		/// 待った後に instance が出来ていれば DestroyNow が ReleaseInstance で正しく回収できる。
		/// </summary>
		async UniTask SettleHandleAsync()
		{
			if (!_ownsHandle || !_handle.IsValid()) return;
			while (!_handle.IsDone) await UniTask.Yield(PlayerLoopTiming.Update, CancellationToken.None);
			if (_instanceGo == null && _handle.Status == AsyncOperationStatus.Succeeded)
			{
				_instanceGo = _handle.Result;
				if (_instanceGo != null) _instance = _instanceGo.GetComponent<ScreenEffect>();
			}
		}

		void DestroyNow()
		{
			// 借りた order を返却（lease は instantiate 成功時のみ立つので _host は非 null が保証される）。
			if (_hasLease)
			{
				_host.ReleaseOrder(_leasedOrder);
				_hasLease = false;
			}
			if (_instanceGo != null)
			{
				if (_ownsHandle && _handle.IsValid())
				{
					// InstantiateAsync 経由は ReleaseInstance で参照カウントを返す
					Addressables.ReleaseInstance(_instanceGo);
				}
				else if (Application.isPlaying)
				{
					UnityEngine.Object.Destroy(_instanceGo);
				}
				else
				{
					// EditMode（テスト等）では Destroy が使えないため即時破棄する
					UnityEngine.Object.DestroyImmediate(_instanceGo);
				}
				_instanceGo = null;
				_instance = null;
			}
			else if (_ownsHandle && _handle.IsValid())
			{
				Addressables.Release(_handle);
			}
			_ownsHandle = false;
		}
	}
}
