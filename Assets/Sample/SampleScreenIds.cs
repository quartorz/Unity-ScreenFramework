using ScreenFramework;
using System;

namespace Sample
{
	/// <summary>
	/// サンプルで使う Addressables キー。Addressables Group のアドレスと一致させる。
	/// </summary>
	public static class SampleAddresses
	{
		public const string TitleView         = "Views/TitleView";
		public const string HomeView          = "Views/HomeView";
		public const string ProfileView       = "Views/ProfileView";
		public const string InputDialog       = "Views/InputDialog";
		public const string MessageDialogView = "Views/MessageDialogView";
		public const string GachaTopView      = "Views/GachaTopView";
		public const string GachaResultView   = "Views/GachaResultView";
	}

	public record SampleScreenId<TMockView, TView, TPresenter> : AddressableScreenId<TMockView>
		where TMockView : class, new()
		where TPresenter : IScreenPresenter
	{
		protected override string AddressableKey => $"Views/{typeof(TView).Name}";
		protected override IScreenPresenter MakePresenter() => Activator.CreateInstance<TPresenter>();
	}

	public sealed record TitleScreenId : SampleScreenId<MockView.Sample.MockTitleView, TitleView, TitlePresenter>;
	public sealed record HomeScreenId : SampleScreenId<MockView.Sample.MockHomeView, HomeView, HomePresenter>;
	public sealed record GachaTopScreenId : SampleScreenId<MockView.Sample.MockGachaTopView, GachaTopView, GachaTopPresenter>;

	public sealed record ProfileScreenId(string UserId) : SampleScreenId<MockView.Sample.MockProfileView, ProfileView, ProfilePresenter>
	{
		protected override IScreenPresenter MakePresenter() => new ProfilePresenter(UserId);
	}

	public sealed record GachaResultScreenId(Sample.Api.GachaPullResponse Result)
		: SampleScreenId<MockView.Sample.MockGachaResultView, GachaResultView, GachaResultPresenter>
	{
		protected override IScreenPresenter MakePresenter() => new GachaResultPresenter(Result);
	}
}
