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
			GachaRoutes.Register(server);
			server.Start();
			Instance = server;

			Application.quitting += () =>
			{
				Instance?.Dispose();
				Instance = null;
			};
		}
	}
}
