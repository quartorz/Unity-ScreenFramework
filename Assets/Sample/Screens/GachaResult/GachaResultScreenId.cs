using ScreenFramework;

namespace Sample
{
	public sealed record GachaResultScreenId(Sample.Api.GachaPullResponse Result)
		: SampleScreenId<MockView.Sample.MockGachaResultView, GachaResultView, GachaResultPresenter>
	{
		protected override IScreenPresenter MakePresenter() => new GachaResultPresenter(Result);
	}
}
