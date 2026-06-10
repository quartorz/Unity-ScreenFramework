using System;
using Sample.Api;

namespace Sample.Debug
{
	/// <summary>
	/// デバッグ起動用の固定ダミーレスポンス置き場。値は LocalServer の初期データをミラーしている。
	/// <see cref="DebugScreenIds"/> の直起動ペイロードと各 Debug Service の戻り値が
	/// ここを共有することで、二重管理を避ける。
	/// 毎回新しいインスタンスを返す（呼び出し側が money 等を書き換えても他に影響しない）。
	/// </summary>
	public static class DummyResponses
	{
		public const string UserId = "user-001";

		public static UserInfoResponse UserInfo() => new UserInfoResponse
		{
			userId = UserId,
			name = "名無しさん",
			level = 1,
			money = 5000,
		};

		public static ProfileResponse Profile() => new ProfileResponse
		{
			userId = UserId,
			name = "名無しさん",
			level = 1,
		};

		public static BootstrapMasterResponse MasterBootstrap() => new BootstrapMasterResponse
		{
			items = new[]
			{
				new ItemMasterResponse { id = 1,  code = "sword_wood",     name = "木の剣",         rarity = 1 },
				new ItemMasterResponse { id = 2,  code = "sword_iron",     name = "鉄の剣",         rarity = 2 },
				new ItemMasterResponse { id = 3,  code = "sword_silver",   name = "銀の剣",         rarity = 3 },
				new ItemMasterResponse { id = 4,  code = "sword_mithril",  name = "ミスリルの剣",   rarity = 4 },
				new ItemMasterResponse { id = 5,  code = "sword_excalibur", name = "エクスカリバー", rarity = 5 },
				new ItemMasterResponse { id = 6,  code = "shield_wood",    name = "木の盾",         rarity = 1 },
				new ItemMasterResponse { id = 7,  code = "shield_iron",    name = "鉄の盾",         rarity = 2 },
				new ItemMasterResponse { id = 8,  code = "shield_holy",    name = "聖なる盾",       rarity = 4 },
				new ItemMasterResponse { id = 9,  code = "potion_heal",    name = "ポーション",     rarity = 1 },
				new ItemMasterResponse { id = 10, code = "potion_elixir",  name = "エリクサー",     rarity = 5 },
			},
		};

		public static GachaListResponse GachaList() => new GachaListResponse
		{
			gachas = new[]
			{
				new GachaInfoResponse { id = "standard", name = "スタンダードガチャ", cost1 = 300,  cost10 = 3000 },
				new GachaInfoResponse { id = "premium",  name = "プレミアムガチャ",   cost1 = 1000, cost10 = 9500 },
				new GachaInfoResponse { id = "limited",  name = "リミテッドガチャ",   cost1 = 1500, cost10 = 14000 },
			},
		};

		/// <summary>
		/// 固定の排出結果。先頭を最高レアにしてあるので、1 連でも最高レア演出
		/// （<see cref="Sample.Effects.GachaResultMatcher"/> の rarity 判定）が確認できる。
		/// money は直起動時のそれらしい値。Service 経由ではホルダーの現在値から上書きされる。
		/// </summary>
		public static GachaPullResponse GachaPull(int count)
		{
			var pool = new[]
			{
				new PulledItemResponse { code = "sword_excalibur", name = "エクスカリバー", rarity = 5 },
				new PulledItemResponse { code = "potion_heal",     name = "ポーション",     rarity = 1 },
				new PulledItemResponse { code = "sword_wood",      name = "木の剣",         rarity = 1 },
				new PulledItemResponse { code = "sword_iron",      name = "鉄の剣",         rarity = 2 },
				new PulledItemResponse { code = "sword_silver",    name = "銀の剣",         rarity = 3 },
				new PulledItemResponse { code = "sword_mithril",   name = "ミスリルの剣",   rarity = 4 },
				new PulledItemResponse { code = "shield_iron",     name = "鉄の盾",         rarity = 2 },
				new PulledItemResponse { code = "shield_holy",     name = "聖なる盾",       rarity = 4 },
				new PulledItemResponse { code = "potion_elixir",   name = "エリクサー",     rarity = 5 },
				new PulledItemResponse { code = "shield_wood",     name = "木の盾",         rarity = 1 },
			};
			var items = new PulledItemResponse[Math.Clamp(count, 1, pool.Length)];
			Array.Copy(pool, items, items.Length);
			return new GachaPullResponse
			{
				items = items,
				money = 2000,
			};
		}
	}
}
