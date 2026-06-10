using Cysharp.Threading.Tasks;
using Sample.Api;
using Sample.Api.Net;

namespace Sample.Debug
{
	/// <summary>
	/// <see cref="IProfileService"/> のデバッグ実装。
	/// Get は <see cref="UserDataHolder"/> に Info が入っていればそれを優先し
	/// （Title 経由・Charge 後などの状態を反映）、空なら固定ダミーを返す。
	/// Post は LocalServer 同様 name / level だけ反映したエコーを返す。
	/// </summary>
	public sealed class DebugProfileService : DebugApiBase, IProfileService
	{
		public DebugProfileService(UserDataHolder userData, DebugScenarioState scenario)
			: base(userData, scenario) { }

		public UniTask<ProfileResponse> Get(string userId, Options opt = default)
			=> Send(() =>
			{
				var info = UserData.Info;
				if (info == null) return DummyResponses.Profile();
				return new ProfileResponse
				{
					userId = info.UserId,
					name = info.Name,
					level = info.Level,
				};
			}, opt);

		public UniTask<ProfileResponse> Post(ProfileRequest req, Options opt = default)
			=> Send(() => new ProfileResponse
			{
				userId = req.userId,
				name = req.name,
				level = req.level,
			}, opt);
	}
}
