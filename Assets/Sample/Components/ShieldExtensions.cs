using Cysharp.Threading.Tasks;

namespace Sample
{
	/// <summary>
	/// 遷移タスクなどに入力遮蔽（<see cref="InputShield"/>）を連動させる拡張。
	/// <c>await ScreenNavigator.Page.Push(id).WithLoadingShield();</c> のように書くと、
	/// タスクの実行中だけ指定 Key の Shield を表示し、完了・例外・キャンセルいずれでも確実に隠す。
	/// <para>
	/// ScreenFramework は遷移を一律にブロックしない（preempt = 遷移中の割り込み設計を壊すため）。
	/// 「どの操作の間どの Shield を出すか」はプロジェクト側がこの拡張で明示的に選ぶ。
	/// 対象 Shield が未登録（シーンに無い）なら no-op としてそのまま待つ。
	/// </para>
	/// </summary>
	public static class ShieldUniTaskExtensions
	{
		public const string DefaultLoadingKey = "loading";

		public static async UniTask WithLoadingShield(this UniTask task, string key = DefaultLoadingKey)
		{
			var shield = ShieldRegistry.Get(key);
			shield?.Show();
			try { await task; }
			finally { shield?.Hide(); }
		}

		public static async UniTask<T> WithLoadingShield<T>(this UniTask<T> task, string key = DefaultLoadingKey)
		{
			var shield = ShieldRegistry.Get(key);
			shield?.Show();
			try { return await task; }
			finally { shield?.Hide(); }
		}
	}
}
