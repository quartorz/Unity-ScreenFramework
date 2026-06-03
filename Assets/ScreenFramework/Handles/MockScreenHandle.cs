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

		public UniTask<IScreenViewInstance> Load(IProgress<float> progress, CancellationToken ct)
		{
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

		public T As<T>() where T : class
		{
			return _mock as T;
		}
	}
}
