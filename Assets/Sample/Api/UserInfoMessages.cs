using System;

namespace Sample.Api
{
	/// <summary>
	/// 起動時に自ユーザー分を一括取得する API のレスポンス。
	/// 今は profile 相当だけだが、サーバ側で所持カード等を将来追加する想定。
	/// </summary>
	[Serializable]
	public class UserInfoResponse
	{
		public string userId;
		public string name;
		public int level;
	}
}
