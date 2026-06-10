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
	/// ライフサイクル hook の呼び出し順序は操作種別で変わる。Push は OnBeforeLoad → OnAfterLoad → OnBeforeExit → ... の順だが、
	/// Pop で下画面が破棄済みなら OnBeforeExit → ... → OnBeforeLoad → OnAfterLoad → OnBeforeEnter の順になり、
	/// 下画面がキャッシュ済み（生存・suspended）の Pop では load hook は一切呼ばれない。
	/// よって OnBeforeLoad が必ず最初に来る前提で初期化を書いてはならない。
	/// 全体の初期化（asset の事前読込など）は <see cref="OnInitialize"/> に書くこと。
	/// </para>
	/// </summary>
	public abstract class ScreenEffect : MonoBehaviour
	{
		/// <summary>
		/// 全体の初期化用 hook。Instantiate 直後、最初のライフサイクル hook より前に
		/// 操作種別によらず必ず一度だけ呼ばれる（キャッシュ済み Pop で load hook が不発の場合でも呼ばれる）。
		/// ctx.Kind / ctx.Reader を見て条件付きの準備をしてもよい。
		/// 例外時は Effect 全体が無効化される（まだ何も表示していないので即 Destroy、遷移は続行）。
		/// </summary>
		public virtual UniTask OnInitialize(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;

		public virtual UniTask OnBeforeLoad(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnAfterLoad(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnBeforeExit(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnAfterExit(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnBeforeEnter(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
		public virtual UniTask OnAfterEnter(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;
	}
}
