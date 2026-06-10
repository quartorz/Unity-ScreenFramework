using Cysharp.Threading.Tasks;
using Sample.Api;
using Sample.Api.Net;

namespace Sample.Debug
{
	/// <summary>
	/// <see cref="IUserService"/> のデバッグ実装。Info は固定ダミー、
	/// Charge は <see cref="UserDataHolder"/> の現在値に加算した結果を返す。
	/// </summary>
	public sealed class DebugUserService : DebugApiBase, IUserService
	{
		public DebugUserService(UserDataHolder userData, DebugScenarioState scenario)
			: base(userData, scenario) { }

		public UniTask<UserInfoResponse> Info(Options opt = default)
			=> Send(DummyResponses.UserInfo, opt);

		public UniTask<ChargeResponse> Charge(ChargeRequest req, Options opt = default)
			=> Send(() => new ChargeResponse { money = UserData.Money + req.amount }, opt);
	}
}
