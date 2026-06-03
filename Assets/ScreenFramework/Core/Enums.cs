namespace ScreenFramework
{
	public enum ScreenCacheMode
	{
		DestroyOnCover,
		KeepOnCover,
	}

	public enum StackMode
	{
		Cover,
		Stack,
	}

	public enum StackInputPolicy
	{
		BlockUnderlying,
		PassThrough,
	}

	public enum InterruptPriority
	{
		/// <summary>
		/// 既定。実行中の遷移を即座にキャンセルして自分が走る。
		/// </summary>
		Preempt,
		/// <summary>
		/// 実行中の遷移の完走を待ってから自分が走る。
		/// </summary>
		Queue,
	}
}
