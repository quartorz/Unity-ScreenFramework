namespace ScreenFramework
{
	/// <summary>
	/// 進行中の遷移操作の種類。Effect / Matcher 側から context 経由で参照し、
	/// 「Push 時カットイン」と「Pop 時フェード」を別 Effect に振り分ける等の細分マッチに使う。
	/// </summary>
	public enum OperationKind
	{
		Push,
		Pop,
		Replace,
		Change,
		Reset,
		PopTo,
		Close,
	}
}
