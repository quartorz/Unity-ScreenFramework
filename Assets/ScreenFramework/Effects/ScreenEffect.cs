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
	/// ロールバック可能ゾーン (Push/Replace の新規 load) の例外は即 Destroy、
	/// 完走必須ゾーン (Exit 以降、および Pop/Close の復元 load) の例外は遷移完了まで Destroy 遅延される。
	/// </para>
	/// <para>
	/// hook の呼び出し順序は操作種別で変わる。Push は OnBeforeLoad → OnAfterLoad → OnBeforeExit → ... の順だが、
	/// Pop で下画面が破棄済みなら OnBeforeExit → ... → OnBeforeLoad → OnAfterLoad → OnBeforeEnter の順になり、
	/// 下画面がキャッシュ済み（生存・suspended）の Pop では load hook は一切呼ばれない。
	/// よって OnBeforeLoad が必ず最初に来る前提で初期化を書いてはならない。
	/// load hook 以外（Exit/Enter）で参照する状態は、その hook 側で遅延初期化するか、
	/// ctx.Kind / ctx.Reader を都度読むこと。
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
