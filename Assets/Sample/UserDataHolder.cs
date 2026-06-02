namespace Sample
{
	/// <summary>
	/// 起動時に取得した「自ユーザーのデータ」をまとめて保持する。
	/// マスタとは別に <see cref="SampleServices.UserData"/> から各画面が読み出す。
	/// 今は <see cref="Info"/> だけだが、所持カード等のリソースを足していく前提。
	/// </summary>
	public sealed class UserDataHolder
	{
		public UserInfo Info { get; private set; }

		public void SetInfo(UserInfo info)
		{
			Info = info;
		}
	}

	public sealed class UserInfo
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public int Level { get; set; }
	}
}
