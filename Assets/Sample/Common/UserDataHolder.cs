using System;

namespace Sample
{
	/// <summary>
	/// 起動時に取得した「自ユーザーのデータ」をまとめて保持する。
	/// マスタとは別に <see cref="SampleRegistry.UserData"/> から各画面が読み出す。
	/// 所持金などは値変化があり得るので、変更時にイベントで通知する。
	/// </summary>
	public sealed class UserDataHolder
	{
		public UserInfo Info { get; private set; }
		public int Money { get; private set; }

		public event Action OnMoneyChanged;

		public void SetInfo(UserInfo info)
		{
			Info = info;
			SetMoney(info != null ? info.Money : 0);
		}

		public void SetMoney(int money)
		{
			if (Money == money) return;
			Money = money;
			OnMoneyChanged?.Invoke();
		}
	}

	public sealed class UserInfo
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public int Level { get; set; }
		public int Money { get; set; }
	}
}
