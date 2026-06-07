using Sample.Api;

namespace Sample
{
	/// <summary>
	/// Sample プロジェクトの通信用 Service バンドル。
	/// 各 Service は <see cref="UserDataHolder"/> を共有していて、
	/// 通信完了時に必要に応じて自分で書き戻す。
	/// </summary>
	public sealed class SampleApiServices
	{
		public IGachaService Gacha { get; }
		public IUserService User { get; }
		public IProfileService Profile { get; }
		public IMasterService Master { get; }

		public SampleApiServices(
			IGachaService gacha,
			IUserService user,
			IProfileService profile,
			IMasterService master)
		{
			Gacha = gacha;
			User = user;
			Profile = profile;
			Master = master;
		}
	}
}
