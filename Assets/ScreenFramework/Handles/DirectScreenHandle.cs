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

		public UniTask<IScreenViewInstance> Load(IProgress<float> progress, CancellationToken ct)
		{
			_instance = Object.Instantiate(_prefab);
			// Awake をトリガーするため一度 active にし、その直後に隠す。
			// Navigator が見せるまでの間 presenter 未配線で Update 等が回り続けないようにする。
			if (!_instance.activeSelf) _instance.SetActive(true);
			_instance.SetActive(false);
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
