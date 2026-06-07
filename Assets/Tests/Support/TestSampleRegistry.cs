using Sample;
using Sample.Api;

namespace Tests
{
	/// <summary>
	/// テストで <see cref="SampleRegistry"/> を組み立てるためのヘルパ。
	/// 渡さなかった Service は MockXxxService が自動で埋まる。
	/// 個別 Mock を保持して assert したい場合は呼び出し側で先に new して渡すこと。
	/// </summary>
	public static class TestSampleRegistry
	{
		public static SampleRegistry AllMocks(
			IGachaService gacha = null,
			IUserService user = null,
			IProfileService profile = null,
			IMasterService master = null,
			UserDataHolder userData = null)
		{
			userData ??= new UserDataHolder();
			var api = new SampleApiServices(
				gacha ?? new MockGachaService(),
				user ?? new MockUserService(),
				profile ?? new MockProfileService(),
				master ?? new MockMasterService());
			return new SampleRegistry(useMockViews: true, api, userData);
		}
	}
}
