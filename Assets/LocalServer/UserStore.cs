namespace LocalServer
{
	/// <summary>
	/// サーバー内 in-memory のユーザー状態。Profile / Gacha / Charge 系 routes から共有される。
	/// 単一プロセスの開発用サーバーなので排他は省略。
	/// </summary>
	static class UserStore
	{
		public static readonly UserRecord User = new UserRecord
		{
			userId = "user-001",
			name   = "名無しさん",
			level  = 1,
			money  = 5000,
		};
	}

	[System.Serializable]
	class UserRecord
	{
		public string userId;
		public string name;
		public int level;
		public int money;
	}
}
