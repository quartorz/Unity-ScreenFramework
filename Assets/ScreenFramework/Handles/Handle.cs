using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ScreenFramework
{
	public interface IScreenHandle
	{
		/// <summary>
		/// View を生成する。<paramref name="stagingParent"/> が指定された場合、その（非アクティブ前提の）親の下に
		/// 生成して描画させないまま返すこと。Navigator が SetParent / SetActive で見せるまでチラつかせないための仕組み。
		/// </summary>
		UniTask<IScreenViewInstance> Load(Transform stagingParent, IProgress<float> progress, CancellationToken ct);
		UniTask Unload(CancellationToken ct);
	}

	public interface IScreenViewInstance
	{
		void SetActive(bool active);
		void SetParent(Transform parent);
		T As<T>() where T : class;

		/// <summary>
		/// この View の Canvas に Render Mode = Screen Space - Camera と Sorting Layer / Order を流し込む。
		/// 実体は View 側の <see cref="IScreenCanvasController"/> 実装へ委譲する（未実装なら no-op）。
		/// </summary>
		void ApplyCanvasSorting(Camera camera, int sortingLayerId, int order);
	}
}
