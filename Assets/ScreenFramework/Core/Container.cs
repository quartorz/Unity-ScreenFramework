using UnityEngine;

namespace ScreenFramework
{
	public interface IScreenContainer
	{
		Transform Root { get; }

		/// <summary>
		/// このコンテナに属する画面 Canvas を Render Mode = Screen Space - Camera で描画するためのカメラ。
		/// 各画面 Canvas の <c>worldCamera</c> に流し込まれる。
		/// </summary>
		Camera RenderCamera { get; }

		/// <summary>このコンテナに属する全画面 Canvas に設定する Sorting Layer の ID。</summary>
		int SortingLayerId { get; }

		/// <summary>スタック最下段（index 0）の画面に与える Sorting Order。</summary>
		int BaseOrder { get; }

		/// <summary>スタックを 1 段上がるごとに加算する Order の刻み幅。画面内の子 Canvas 用に隙間を空けるため大きめ（既定 10）。</summary>
		int OrderStep { get; }
	}
}
