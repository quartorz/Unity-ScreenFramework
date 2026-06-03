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
	///   Enter: SetActive(true) → OnBeforeEnter → WhenAll(transition.End, PlayEnter) → OnAfterEnter
	///   Exit : OnBeforeExit → PlayExit → SetActive(false) → OnAfterExit
	///
	/// Pop の場合、下から戻る画面に対しても Enter が走る（Cover で隠れていた場合のみ。
	/// Stack で常時 visible だった場合は Enter 不要なので呼ばれない）。
	///
	/// View が未実装でも no-op として扱う（Navigator は null 安全に呼ぶ）。
	/// </summary>
	public interface IScreenAnimatedView
	{
		UniTask PlayEnter(CancellationToken ct);
		UniTask PlayExit(CancellationToken ct);
	}
}
