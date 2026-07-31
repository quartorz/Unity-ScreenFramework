using UnityEngine;

namespace ScreenFramework
{
	/// <summary>
	/// 画面 View が自身の Canvas の描画順（Render Mode / カメラ / Sorting Layer / Order）を
	/// 設定するためのオプショナル I/F。Navigator がコンテナのスタック順に応じて算出した値を
	/// <see cref="ApplyCanvasSorting"/> で流し込む。
	///
	/// View MonoBehaviour（または Mock）が実装すると、Navigator が画面の入場・退場・並び替えの
	/// タイミングで自動的に呼ぶ。未実装なら no-op（Navigator は null 安全に呼ぶ）。
	///
	/// 冪等性が要件: reflow で何度も呼ばれるため、実装は受け取った (camera, layer, order) のみに
	/// 依存する純粋な設定とし、状態を溜めないこと。複数 Canvas を持つ画面は実装側で order を
	/// 基準値として子 Canvas にオフセットを振る（<see cref="IScreenContainer.OrderStep"/> 相当の隙間が空いている前提）。
	/// </summary>
	public interface IScreenCanvasController
	{
		void ApplyCanvasSorting(Camera camera, int sortingLayerId, int order);
	}
}
