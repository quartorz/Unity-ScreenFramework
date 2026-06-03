using ScreenFramework;

namespace Sample
{
	public sealed record ProfileScreenId(string UserId) : SampleScreenId<MockView.Sample.MockProfileView, ProfileView, ProfilePresenter>
	{
		protected override IScreenPresenter MakePresenter() => new ProfilePresenter(UserId);
	}
}
