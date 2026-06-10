using System;
using Cysharp.Threading.Tasks;
using Sample.Api;
using Sample.Api.Net;

namespace Sample.Debug
{
	/// <summary>
	/// <see cref="IGachaService"/> のデバッグ実装。固定ダミーを返しつつ、
	/// 所持金の増減だけは <see cref="UserDataHolder"/> の現在値から計算して
	/// LocalServer の挙動（残高不足は 402）をミラーする。
	/// </summary>
	public sealed class DebugGachaService : DebugApiBase, IGachaService
	{
		public DebugGachaService(UserDataHolder userData, DebugScenarioState scenario)
			: base(userData, scenario) { }

		public UniTask<GachaListResponse> List(Options opt = default)
			=> Send(DummyResponses.GachaList, opt);

		public UniTask<GachaPullResponse> Pull(GachaPullRequest req, Options opt = default)
			=> Send(() =>
			{
				var gacha = Array.Find(DummyResponses.GachaList().gachas, g => g.id == req.gachaId)
					?? throw new ApiException(404, new ErrorResponse { code = "debug", message = "ガチャが見つかりません。" }, null, "[Debug] gacha not found");
				var cost = req.count == 10 ? gacha.cost10 : gacha.cost1;
				if (UserData.Money < cost)
				{
					throw new ApiException(402, new ErrorResponse { code = "debug", message = "所持金が足りません。" }, null, "[Debug] not enough money");
				}
				var resp = DummyResponses.GachaPull(req.count);
				resp.money = UserData.Money - cost;
				return resp;
			}, opt);
	}
}
