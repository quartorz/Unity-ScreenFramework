using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ScreenFramework
{
	/// <summary>
	/// Addressables の指定キーからプレハブを Instantiate する Handle。
	/// </summary>
	public sealed class AddressableScreenHandle : IScreenHandle
	{
		readonly object _key;
		GameObject _instance;
		AsyncOperationHandle<GameObject> _handle;

		public AddressableScreenHandle(object key)
		{
			_key = key ?? throw new ArgumentNullException(nameof(key));
		}

		public async UniTask<IScreenViewInstance> Load(IProgress<float> progress, CancellationToken ct)
		{
			_handle = Addressables.InstantiateAsync(_key);
			while (!_handle.IsDone)
			{
				progress?.Report(_handle.PercentComplete);
				await UniTask.Yield(PlayerLoopTiming.Update, ct);
			}
			if (_handle.Status != AsyncOperationStatus.Succeeded)
			{
				throw new InvalidOperationException($"Failed to load Addressable: {_key}");
			}
			_instance = _handle.Result;
			// Instantiate 直後は active で scene root に出ているため、Navigator が SetParent/SetActive で
			// 見せるまでの間 presenter 未配線のまま Awake/OnEnable/Update が走らないよう即座に隠す。
			if (_instance != null && _instance.activeSelf) _instance.SetActive(false);
			return new PrefabScreenViewInstance(_instance);
		}

		public async UniTask Unload(CancellationToken ct)
		{
			// ロード中（preempt 等で polling を抜けた直後）に Unload されると _handle がまだ未完了のことがある。
			// 未完了ハンドルへの Release はバージョン依存で挙動が不定（インスタンスが取り残されうる）なので、
			// 一度決着を待ってから解放する。クリーンアップなので ct では中断しない。
			if (_handle.IsValid() && !_handle.IsDone)
			{
				while (!_handle.IsDone) await UniTask.Yield(PlayerLoopTiming.Update, CancellationToken.None);
			}
			// 巻き戻し中に load が完了していたら、取り残さないようインスタンスを掴む。
			if (_instance == null && _handle.IsValid() && _handle.Status == AsyncOperationStatus.Succeeded)
			{
				_instance = _handle.Result;
			}
			if (_instance != null)
			{
				Addressables.ReleaseInstance(_instance);
				_instance = null;
			}
			else if (_handle.IsValid())
			{
				Addressables.Release(_handle);
			}
		}
	}

	internal sealed class PrefabScreenViewInstance : IScreenViewInstance
	{
		readonly GameObject _go;

		public PrefabScreenViewInstance(GameObject go)
		{
			_go = go;
		}

		public void SetActive(bool active)
		{
			if (_go != null) _go.SetActive(active);
		}

		public void SetParent(Transform parent)
		{
			if (_go != null) _go.transform.SetParent(parent, worldPositionStays: false);
		}

		public T As<T>() where T : class
		{
			if (_go == null) return null;
			if (typeof(T) == typeof(GameObject)) return _go as T;
			return _go.GetComponentInChildren<T>(true);
		}
	}
}
