using UnityEngine;

namespace LocalServer
{
	/// <summary>
	/// /master/bootstrap GET。起動時にクライアントが一括取得するマスタを返す。
	/// マスタ種別を増やしたら <see cref="BootstrapPayload"/> にフィールドを足してここで詰める。
	/// </summary>
	static class BootstrapMasterRoutes
	{
		public static void Register(LocalHttpServer s)
		{
			s.Map("GET", "/master/bootstrap", (req, res) =>
			{
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(BuildPayload()));
			});
		}

		static BootstrapPayload BuildPayload() => new BootstrapPayload
		{
			items = new[]
			{
				new ItemRecord { id = 1,  code = "sword_wood",    name = "木の剣",         rarity = 1 },
				new ItemRecord { id = 2,  code = "sword_iron",    name = "鉄の剣",         rarity = 2 },
				new ItemRecord { id = 3,  code = "sword_silver",  name = "銀の剣",         rarity = 3 },
				new ItemRecord { id = 4,  code = "sword_mithril", name = "ミスリルの剣",   rarity = 4 },
				new ItemRecord { id = 5,  code = "sword_excalibur", name = "エクスカリバー", rarity = 5 },
				new ItemRecord { id = 6,  code = "shield_wood",   name = "木の盾",         rarity = 1 },
				new ItemRecord { id = 7,  code = "shield_iron",   name = "鉄の盾",         rarity = 2 },
				new ItemRecord { id = 8,  code = "shield_holy",   name = "聖なる盾",       rarity = 4 },
				new ItemRecord { id = 9,  code = "potion_heal",   name = "ポーション",     rarity = 1 },
				new ItemRecord { id = 10, code = "potion_elixir", name = "エリクサー",     rarity = 5 },
			},
		};

		[System.Serializable]
		class BootstrapPayload
		{
			public ItemRecord[] items;
		}

		[System.Serializable]
		class ItemRecord
		{
			public int id;
			public string code;
			public string name;
			public int rarity;
		}
	}
}
