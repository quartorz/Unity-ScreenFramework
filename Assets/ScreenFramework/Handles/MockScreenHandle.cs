using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ScreenFramework
{
	/// <summary>
	/// プレハブを生成せず、TMock のインスタンスを View として返す。テスト・モック用。
	/// </summary>
	public sealed class MockScreenHandle<TMock> : IScreenHandle where TMock : class, new()
	{
		TMock _mock;

		public UniTask<IScreenViewInstance> Load(Transform stagingParent, IProgress<float> progress, CancellationToken ct)
		{
			// Mock は GameObject を生成しないので staging 親は不要。
			_mock = new TMock();
			return UniTask.FromResult<IScreenViewInstance>(new MockScreenViewInstance(_mock));
		}

		public UniTask Unload(CancellationToken ct)
		{
			_mock = null;
			return UniTask.CompletedTask;
		}
	}

	internal sealed class MockScreenViewInstance : IScreenViewInstance
	{
		readonly object _mock;

		public MockScreenViewInstance(object mock)
		{
			_mock = mock;
		}

		public void SetActive(bool active) { /* no-op */ }
		public void SetParent(Transform parent) { /* no-op */ }

		public void ApplyCanvasSorting(Camera camera, int sortingLayerId, int order)
		{
			// Mock View が IScreenCanvasController を実装していれば委譲する（テストで sorting を観測するため）。
			(_mock as IScreenCanvasController)?.ApplyCanvasSorting(camera, sortingLayerId, order);
		}

		public T As<T>() where T : class
		{
			return _mock as T;
		}
	}
}
