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

		public AddressableScreenHandle(object key)
		{
			_key = key ?? throw new ArgumentNullException(nameof(key));
		}

		public async UniTask<IScreenViewInstance> Load(IProgress<float> progress, CancellationToken ct)
		{
			var op = Addressables.InstantiateAsync(_key);
			while (!op.IsDone)
			{
				progress?.Report(op.PercentComplete);
				await UniTask.Yield(PlayerLoopTiming.Update, ct);
			}
			if (op.Status != AsyncOperationStatus.Succeeded)
			{
				throw new InvalidOperationException($"Failed to load Addressable: {_key}");
			}
			_instance = op.Result;
			return new PrefabScreenViewInstance(_instance);
		}

		public UniTask Unload(CancellationToken ct)
		{
			if (_instance != null)
			{
				Addressables.ReleaseInstance(_instance);
				_instance = null;
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
