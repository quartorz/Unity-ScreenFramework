using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ScreenFramework
{
	/// <summary>
	/// GameObject 参照を直接渡してインスタンス化する Handle。
	/// 主にサンプル・テスト・小規模アプリ向け。
	/// </summary>
	public sealed class DirectScreenHandle : IScreenHandle
	{
		readonly GameObject _prefab;
		GameObject _instance;

		public DirectScreenHandle(GameObject prefab)
		{
			_prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
		}

		public UniTask<IScreenViewInstance> Load(Transform stagingParent, IProgress<float> progress, CancellationToken ct)
		{
			if (stagingParent != null)
			{
				// 非アクティブな staging 親の下に生成 → 描画も Awake/OnEnable も走らないまま返す。
				// Navigator が SetParent/SetActive で見せた時に初めて Awake が走る（presenter 配線後）。
				_instance = Object.Instantiate(_prefab, stagingParent);
				if (_instance.activeSelf) _instance.SetActive(false);
			}
			else
			{
				_instance = Object.Instantiate(_prefab);
				// staging が無い場合のフォールバック: Awake をトリガーするため一度 active にし、その直後に隠す。
				if (!_instance.activeSelf) _instance.SetActive(true);
				_instance.SetActive(false);
			}
			return UniTask.FromResult<IScreenViewInstance>(new PrefabScreenViewInstance(_instance));
		}

		public UniTask Unload(CancellationToken ct)
		{
			if (_instance != null)
			{
				Object.Destroy(_instance);
				_instance = null;
			}
			return UniTask.CompletedTask;
		}
	}
}
