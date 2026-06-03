using UnityEngine;

namespace LocalServer
{
	/// <summary>
	/// /user/info GET、/profile GET/POST、/user/charge POST。
	/// 全て <see cref="UserStore"/> に対する読み書き。
	/// </summary>
	static class ProfileRoutes
	{
		public static void Register(LocalHttpServer s)
		{
			s.Map("GET", "/user/info", (req, res) =>
			{
				// 起動時の自ユーザー一括取得。今は profile + money。将来カード等を含める。
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(UserStore.User));
			});

			s.Map("GET", "/profile", (req, res) =>
			{
				var userId = req.QueryString["userId"];
				if (string.IsNullOrEmpty(userId) || userId != UserStore.User.userId)
				{
					res.StatusCode = 404;
					LocalHttpServer.WriteText(res, "user not found");
					return;
				}
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(UserStore.User));
			});

			s.Map("POST", "/profile", (req, res) =>
			{
				var body = LocalHttpServer.ReadBody(req);
				var incoming = JsonUtility.FromJson<UserRecord>(body);
				if (incoming == null)
				{
					res.StatusCode = 400;
					LocalHttpServer.WriteText(res, "invalid body");
					return;
				}
				// userId / money は POST /profile では変更不可
				UserStore.User.name = incoming.name;
				UserStore.User.level = incoming.level;
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(UserStore.User));
			});

			s.Map("POST", "/user/charge", (req, res) =>
			{
				var body = LocalHttpServer.ReadBody(req);
				var incoming = JsonUtility.FromJson<ChargeRequest>(body);
				if (incoming == null || incoming.amount <= 0)
				{
					res.StatusCode = 400;
					LocalHttpServer.WriteText(res, "invalid amount");
					return;
				}
				UserStore.User.money += incoming.amount;
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(new ChargeResponse { money = UserStore.User.money }));
			});
		}

		[System.Serializable]
		class ChargeRequest { public int amount; }

		[System.Serializable]
		class ChargeResponse { public int money; }
	}
}
