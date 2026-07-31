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

		public async UniTask<IScreenViewInstance> Load(Transform stagingParent, IProgress<float> progress, CancellationToken ct)
		{
			// 非アクティブな staging 親の下で生成し、描画されないまま受け取る。Navigator が SetParent/SetActive で
			// 見せるまでチラつかせない（staging が無い場合は従来どおりシーン直下に生成）。
			_handle = stagingParent != null
				? Addressables.InstantiateAsync(_key, stagingParent)
				: Addressables.InstantiateAsync(_key);
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
			// staging 親の下では activeInHierarchy=false なので Awake/OnEnable はまだ走っていない。
			// 本来の親へ移しても見えないよう activeSelf も落としておき、Navigator が SetParent/SetActive で
			// 見せるまで（presenter 配線後）Awake/OnEnable/Update を走らせない。
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
			if (_go == null) return;
			_go.transform.SetParent(parent, worldPositionStays: false);

			if (_go.transform is RectTransform rt)
			{
				rt.anchorMin = Vector2.zero;
				rt.anchorMax = Vector2.one;
				rt.sizeDelta = Vector2.zero;
				rt.anchoredPosition = Vector2.zero;
			}
		}

		public T As<T>() where T : class
		{
			if (_go == null) return null;
			if (typeof(T) == typeof(GameObject)) return _go as T;
			return _go.GetComponentInChildren<T>(true);
		}

		public void ApplyCanvasSorting(Camera camera, int sortingLayerId, int order)
		{
			if (_go == null) return;
			var controller = _go.GetComponentInChildren<IScreenCanvasController>(true);
			controller?.ApplyCanvasSorting(camera, sortingLayerId, order);
		}
	}
}
