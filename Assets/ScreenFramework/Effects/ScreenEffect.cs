using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ScreenFramework
{
	/// <summary>
	/// 画面遷移演出の MonoBehaviour 基底。Presenter のライフサイクル 6 hook と同名・同タイミングで
	/// override 可能なフックを提供する。全 hook はデフォルト空実装で、override したものだけが動く。
	/// <para>
	/// Effect prefab は遷移ごとに Instantiate され、完了時に Destroy される（pooling なし）。
	/// 同じ Effect インスタンス上で hook 間にフィールドを保持してよい。
	/// 自前で生成した GameObject は Effect prefab の子に置くこと（Destroy 時に一括掃除される）。
	/// </para>
	/// <para>
	/// 例外を投げると framework 側がログ + 残 hook skip + 遷移続行で吸収する。
	/// ロールバック可能ゾーン (Load〜OnAfterLoad) の例外は即 Destroy、
	/// 完走必須ゾーン (Exit 以降) の例外は遷移完了まで Destroy 遅延される。
	/// </para>
	/// </summary>
	public abstract class ScreenEffect : MonoBehaviour
	{
		public virtual UniTask OnBeforeLoad(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnAfterLoad(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnBeforeExit(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnAfterExit(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnBeforeEnter(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnAfterEnter(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
	}
}
