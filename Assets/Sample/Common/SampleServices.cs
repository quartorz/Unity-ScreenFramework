using Sample.Api;
using ScreenFramework;

namespace Sample
{
	public sealed class SampleServices : ScreenServices
	{
		public IApiClient Api { get; }

		/// <summary>
		/// 起動時にタイトル画面で <see cref="IApiClient.GetBootstrapMaster"/> から流し込まれる。
		/// 各画面はここから読み取り専用で参照する。
		/// </summary>
		public ItemMasterStore Items { get; }

		/// <summary>
		/// 起動時にタイトル画面で <see cref="IApiClient.GetUserInfo"/> から流し込まれる、
		/// 自ユーザーの「Info / 所持リソース」等をまとめた holder。
		/// </summary>
		public UserDataHolder UserData { get; }

		public SampleServices(bool useMockViews, IApiClient api)
			: base(useMockViews)
		{
			Api = api;
			Items = new ItemMasterStore();
			UserData = new UserDataHolder();
		}
	}
}
