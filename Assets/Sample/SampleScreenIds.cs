using ScreenFramework;

namespace Sample
{
	/// <summary>
	/// サンプルで使う Addressables キー。Addressables Group のアドレスと一致させる。
	/// </summary>
	public static class SampleAddresses
	{
		public const string HomeView   = "Views/HomeView";
		public const string DetailView = "Views/DetailView";
	}

	public sealed record HomeScreenId : AddressableScreenId<MockView.Sample.MockHomeView>
	{
		protected override string AddressableKey => SampleAddresses.HomeView;
		protected override IScreenPresenter MakePresenter() => new HomePresenter();
	}

	public sealed record DetailScreenId(string UserId) : AddressableScreenId<MockView.Sample.MockDetailView>
	{
		protected override string AddressableKey => SampleAddresses.DetailView;
		protected override IScreenPresenter MakePresenter() => new DetailPresenter(UserId);
	}
}
