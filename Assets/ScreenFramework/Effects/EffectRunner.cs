using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ScreenFramework
{
	/// <summary>
	/// 遷移 1 回分の Effect ライフサイクルを Navigator から駆動するためのラッパ。
	/// Matcher 解決済みの prefab を Load → Instantiate → 6 hook を順次呼び出す責任を持つ。
	/// 全段で例外を吸収（ログ + 無効化）し、本筋の遷移を絶対に止めない。
	/// </summary>
	internal sealed class EffectRunner
	{
		readonly AssetReferenceGameObject _prefabRef;
		readonly Transform _parent;
		readonly ITransitionContext _ctx;

		ScreenEffect _instance;
		GameObject _instanceGo;
		AsyncOperationHandle<GameObject> _handle;
		bool _disabled;          // 例外発生後の以降 hook skip 用
		bool _deferDestroy;      // 完走必須ゾーンで例外 → 遷移完了まで生かす
		bool _ownsHandle;        // InstantiateAsync で取った handle を ReleaseInstance で返す必要があるか

		public bool IsAlive => _instance != null;

		public EffectRunner(AssetReferenceGameObject prefabRef, Transform parent, ITransitionContext ctx)
		{
			_prefabRef = prefabRef;
			_parent = parent;
			_ctx = ctx;
		}

		/// <summary>
		/// Effect prefab の Load + Instantiate。失敗時は Disabled 化して以降 no-op。
		/// </summary>
		public async UniTask LoadAndInstantiateAsync(CancellationToken ct)
		{
			try
			{
				_handle = _prefabRef.InstantiateAsync(_parent);
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
				_instance = go.GetComponent<ScreenEffect>();
				if (_instance == null)
				{
					Debug.LogError($"[ScreenFramework] Effect prefab '{go.name}' has no ScreenEffect component. Disabled.");
					DestroyNow();
					return;
				}
			}
			catch (OperationCanceledException)
			{
				DestroyNow();
				throw;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				DestroyNow();
				_disabled = true;
			}
		}

		public UniTask OnBeforeLoad(CancellationToken ct) => RunHookRollbackZone(_instance != null ? _instance.OnBeforeLoad : null, ct);
		public UniTask OnAfterLoad(CancellationToken ct)  => RunHookRollbackZone(_instance != null ? _instance.OnAfterLoad  : null, ct);
		public UniTask OnBeforeExit(CancellationToken ct) => RunHookCommitZone(_instance != null ? _instance.OnBeforeExit : null, ct);
		public UniTask OnAfterExit(CancellationToken ct)  => RunHookCommitZone(_instance != null ? _instance.OnAfterExit  : null, ct);
		public UniTask OnBeforeEnter(CancellationToken ct)=> RunHookCommitZone(_instance != null ? _instance.OnBeforeEnter: null, ct);
		public UniTask OnAfterEnter(CancellationToken ct) => RunHookCommitZone(_instance != null ? _instance.OnAfterEnter : null, ct);

		/// <summary>
		/// 遷移完了時の最終クリーンアップ。完走必須ゾーンで Destroy 遅延した分もここで消える。
		/// </summary>
		public void Finish()
		{
			DestroyNow();
		}

		async UniTask RunHookRollbackZone(Func<ITransitionContext, CancellationToken, UniTask> hook, CancellationToken ct)
		{
			if (_disabled || hook == null) return;
			try
			{
				await hook(_ctx, ct);
			}
			catch (OperationCanceledException)
			{
				// preempt 等で巻き戻し中 → 即 Destroy で巻き戻し継ぎ目もない
				DestroyNow();
				_disabled = true;
				throw;
			}
			catch (Exception e)
			{
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
				// 完走必須ゾーン: 残 hook skip + Destroy は遷移完了まで遅延（継ぎ目隠しの責務があり得るため）
				Debug.LogException(e);
				_disabled = true;
				_deferDestroy = true;
			}
		}

		void Disable(string reason)
		{
			Debug.LogWarning($"[ScreenFramework] {reason}. Effect disabled, transition continues.");
			DestroyNow();
			_disabled = true;
		}

		void DestroyNow()
		{
			if (_instanceGo != null)
			{
				if (_ownsHandle && _handle.IsValid())
				{
					// InstantiateAsync 経由は ReleaseInstance で参照カウントを返す
					Addressables.ReleaseInstance(_instanceGo);
				}
				else
				{
					UnityEngine.Object.Destroy(_instanceGo);
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
