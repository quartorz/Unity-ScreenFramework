using Sample.Api;
using ScreenFramework;

namespace Sample
{
	/// <summary>
	/// Sample プロジェクト用の Registry。
	/// 過去プロジェクト由来の語彙で、各 Service（proto 由来想定）と
	/// アプリ横断の Holder/Store をまとめて保持する DI バンドル。
	/// 静的ファサード <see cref="ScreenNavigator"/> 初期化時に渡す。
	/// </summary>
	public sealed class SampleRegistry : ScreenServices
	{
		public IGachaService Gacha { get; }
		public IUserService User { get; }
		public IProfileService Profile { get; }
		public IMasterService Master { get; }

		/// <summary>
		/// 起動時にタイトル画面で <see cref="IMasterService.Bootstrap"/> から流し込まれる。
		/// 各画面はここから読み取り専用で参照する。
		/// </summary>
		public ItemMasterStore Items { get; }

		/// <summary>
		/// 起動時にタイトル画面で <see cref="IUserService.Info"/> から流し込まれる、
		/// 自ユーザーの「Info / 所持リソース」等をまとめた holder。
		/// </summary>
		public UserDataHolder UserData { get; }

		public SampleRegistry(
			bool useMockViews,
			IGachaService gacha,
			IUserService user,
			IProfileService profile,
			IMasterService master)
			: base(useMockViews)
		{
			Gacha = gacha;
			User = user;
			Profile = profile;
			Master = master;
			Items = new ItemMasterStore();
			UserData = new UserDataHolder();
		}
	}
}
