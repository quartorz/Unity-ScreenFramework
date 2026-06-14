using ScreenFramework;
using UnityEngine;

namespace Sample.Effects
{
	/// <summary>
	/// Effect Registry の行で「この遷移の from / to に該当するか」を判定する SO 基底。
	/// 利用者は派生 SO を作って <see cref="Match"/> で型 + 値の述語を書く。
	/// 例: <c>id is BattleResultId r &amp;&amp; r.IsWin == IsWin</c>。
	/// <para>
	/// kind 条件で振り分けたい場合は <paramref name="ctx"/> から <see cref="ITransitionContext.Kind"/> を読む。
	/// SO 自体に kind フィルタ列は持たせない方針（grill 決定）。
	/// </para>
	/// </summary>
	public abstract class ScreenMatcher : ScriptableObject
	{
		public abstract bool Match(IScreenIdentifier id, ITransitionContext ctx);
	}
}
