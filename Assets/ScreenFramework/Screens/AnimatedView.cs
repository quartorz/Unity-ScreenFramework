using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// 画面ごとの Enter / Exit アニメを View 側で実装するためのオプショナル I/F。
	/// View MonoBehaviour（または Mock）が実装すると、Navigator がライフサイクルの
	/// 該当タイミングで自動的に await する。
	///
	/// タイミング:
	///   Enter: SetActive(true) → OnBeforeShow → PlayEnter → OnAfterShow
	///   Exit : OnBeforeHide → PlayExit → SetActive(false) → OnAfterHide
	///
	/// Pop の場合、下から戻る画面に対しても Enter が走る（Cover で隠れていた場合のみ。
	/// Stack で常時 visible だった場合は Enter 不要なので呼ばれない）。
	/// 既に非表示で保持されていた画面（KeepOnCover で suspend 中）の退場では、見えていないので
	/// PlayExit は呼ばれない（DismissAll / Reset 等で隠れたまま破棄される場合）。
	///
	/// View が未実装でも no-op として扱う（Navigator は null 安全に呼ぶ）。
	/// </summary>
	public interface IScreenAnimatedView
	{
		UniTask PlayEnter(CancellationToken ct);
		UniTask PlayExit(CancellationToken ct);
	}
}
