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
			return new PrefabScreenViewInstance(_instance);
		}

		public UniTask Unload(CancellationToken ct)
		{
			if (_instance != null)
			{
				Addressables.ReleaseInstance(_instance);
				_instance = null;
			}
			else if (_handle.IsValid())
			{
				Addressables.Release(_handle);
			}
			return UniTask.CompletedTask;
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
