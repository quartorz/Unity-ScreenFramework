using ScreenFramework;

namespace Sample
{
	/// <summary>
	/// Sample プロジェクト用の Registry。
	/// 通信用の <see cref="SampleApiServices"/> と、アプリ横断の Holder/Store をまとめて保持する DI バンドル。
	/// 静的ファサード <see cref="ScreenNavigator"/> 初期化時に渡す。
	/// </summary>
	public sealed class SampleRegistry : ScreenServices
	{
		public SampleApiServices Api { get; }

		/// <summary>
		/// 起動時にタイトル画面で <see cref="Sample.Api.IMasterService.Bootstrap"/> から流し込まれる。
		/// 各画面はここから読み取り専用で参照する。
		/// </summary>
		public ItemMasterStore Items { get; }

		/// <summary>
		/// 起動時にタイトル画面で <see cref="Sample.Api.IUserService.Info"/> から流し込まれる、
		/// 自ユーザーの「Info / 所持リソース」等をまとめた holder。
		/// 通信完了時に書き戻すために各 Service にも共有されている。
		/// </summary>
		public UserDataHolder UserData { get; }

		public SampleRegistry(bool useMockViews, SampleApiServices api, UserDataHolder userData)
			: base(useMockViews)
		{
			Api = api;
			UserData = userData;
			Items = new ItemMasterStore();
		}
	}
}
