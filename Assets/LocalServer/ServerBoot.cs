using System.Net;
using UnityEngine;

namespace LocalServer
{
	/// <summary>
	/// アプリ起動と同時にサーバーを立ち上げ、終了時に停止する。
	/// クライアントは <see cref="Instance"/>.BaseUrl を読んで接続する。
	/// </summary>
	public static class ServerBoot
	{
		public static LocalHttpServer Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Boot()
		{
			if (Instance != null) return;

			var server = new LocalHttpServer();
			ProfileRoutes.Register(server);
			BootstrapMasterRoutes.Register(server);
			server.Start();
			Instance = server;

			Application.quitting += () =>
			{
				Instance?.Dispose();
				Instance = null;
			};
		}
	}

	/// <summary>
	/// /profile の GET / POST を提供。サーバー内 in-memory ストアに対して読み書きする。
	/// </summary>
	static class ProfileRoutes
	{
		static ProfileRecord _profile = new ProfileRecord
		{
			userId = "user-001",
			name   = "名無しさん",
			level  = 1,
		};

		public static void Register(LocalHttpServer s)
		{
			s.Map("GET", "/user/info", (req, res) =>
			{
				// 起動時の自ユーザー一括取得。今は profile と同形だが、将来カード等を含める。
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(_profile));
			});

			s.Map("GET", "/profile", (req, res) =>
			{
				var userId = req.QueryString["userId"];
				if (string.IsNullOrEmpty(userId) || userId != _profile.userId)
				{
					res.StatusCode = 404;
					LocalHttpServer.WriteText(res, "user not found");
					return;
				}
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(_profile));
			});

			s.Map("POST", "/profile", (req, res) =>
			{
				var body = LocalHttpServer.ReadBody(req);
				var incoming = JsonUtility.FromJson<ProfileRecord>(body);
				if (incoming == null)
				{
					res.StatusCode = 400;
					LocalHttpServer.WriteText(res, "invalid body");
					return;
				}
				incoming.userId = _profile.userId; // userId は変更不可
				_profile = incoming;
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(_profile));
			});
		}

		[System.Serializable]
		class ProfileRecord
		{
			public string userId;
			public string name;
			public int level;
		}
	}
}
