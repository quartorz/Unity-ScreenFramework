using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ScreenFramework
{
	public interface IScreenHandle
	{
		UniTask<IScreenViewInstance> Load(IProgress<float> progress, CancellationToken ct);
		UniTask Unload(CancellationToken ct);
	}

	public interface IScreenViewInstance
	{
		void SetActive(bool active);
		void SetParent(Transform parent);
		T As<T>() where T : class;
	}
}
