using System.Collections.Generic;
using UnityEngine;

namespace LocalServer
{
	/// <summary>
	/// /gacha/list GET、/gacha/pull POST。
	/// ガチャ一覧は in-memory で固定 3 種類、コスト・排出プールを各ガチャが持つ。
	/// </summary>
	static class GachaRoutes
	{
		static readonly GachaDef[] _gachas =
		{
			new GachaDef
			{
				id = "standard", name = "スタンダードガチャ", cost1 = 300, cost10 = 3000,
				pool = new[]
				{
					new GachaItemDef { code = "potion_heal",  name = "ポーション",   rarity = 1, weight = 60 },
					new GachaItemDef { code = "sword_wood",   name = "木の剣",       rarity = 1, weight = 25 },
					new GachaItemDef { code = "sword_iron",   name = "鉄の剣",       rarity = 2, weight = 12 },
					new GachaItemDef { code = "sword_silver", name = "銀の剣",       rarity = 3, weight = 3 },
				}
			},
			new GachaDef
			{
				id = "premium", name = "プレミアムガチャ", cost1 = 1000, cost10 = 9500,
				pool = new[]
				{
					new GachaItemDef { code = "potion_heal",    name = "ポーション",       rarity = 1, weight = 30 },
					new GachaItemDef { code = "sword_iron",     name = "鉄の剣",           rarity = 2, weight = 35 },
					new GachaItemDef { code = "sword_silver",   name = "銀の剣",           rarity = 3, weight = 25 },
					new GachaItemDef { code = "sword_mithril",  name = "ミスリルの剣",     rarity = 4, weight = 8 },
					new GachaItemDef { code = "sword_excalibur",name = "エクスカリバー",   rarity = 5, weight = 2 },
				}
			},
			new GachaDef
			{
				id = "limited", name = "リミテッドガチャ", cost1 = 1500, cost10 = 14000,
				pool = new[]
				{
					new GachaItemDef { code = "sword_silver",  name = "銀の剣",         rarity = 3, weight = 40 },
					new GachaItemDef { code = "sword_mithril", name = "ミスリルの剣",   rarity = 4, weight = 30 },
					new GachaItemDef { code = "shield_holy",   name = "聖なる盾",       rarity = 4, weight = 20 },
					new GachaItemDef { code = "potion_elixir", name = "エリクサー",     rarity = 5, weight = 7 },
					new GachaItemDef { code = "sword_excalibur",name = "エクスカリバー",rarity = 5, weight = 3 },
				}
			},
		};

		static readonly System.Random _rng = new System.Random();

		public static void Register(LocalHttpServer s)
		{
			s.Map("GET", "/gacha/list", (req, res) =>
			{
				var payload = new GachaListResponse
				{
					gachas = System.Array.ConvertAll(_gachas, g => new GachaInfo
					{
						id = g.id, name = g.name, cost1 = g.cost1, cost10 = g.cost10,
					}),
				};
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(payload));
			});

			s.Map("POST", "/gacha/pull", (req, res) =>
			{
				var body = LocalHttpServer.ReadBody(req);
				var incoming = JsonUtility.FromJson<PullRequest>(body);
				if (incoming == null || (incoming.count != 1 && incoming.count != 10))
				{
					res.StatusCode = 400;
					LocalHttpServer.WriteText(res, "invalid count");
					return;
				}
				var def = System.Array.Find(_gachas, g => g.id == incoming.gachaId);
				if (def == null)
				{
					res.StatusCode = 404;
					LocalHttpServer.WriteText(res, "gacha not found");
					return;
				}
				var cost = incoming.count == 10 ? def.cost10 : def.cost1;
				if (UserStore.User.money < cost)
				{
					res.StatusCode = 402;
					LocalHttpServer.WriteText(res, "not enough money");
					return;
				}
				UserStore.User.money -= cost;

				var items = new List<PulledItem>(incoming.count);
				for (var i = 0; i < incoming.count; i++)
				{
					var pick = Roll(def.pool);
					items.Add(new PulledItem { code = pick.code, name = pick.name, rarity = pick.rarity });
				}
				LocalHttpServer.WriteJson(res, JsonUtility.ToJson(new PullResponse
				{
					items = items.ToArray(),
					money = UserStore.User.money,
				}));
			});
		}

		static GachaItemDef Roll(GachaItemDef[] pool)
		{
			var total = 0;
			foreach (var p in pool) total += p.weight;
			var r = _rng.Next(0, total);
			var acc = 0;
			foreach (var p in pool)
			{
				acc += p.weight;
				if (r < acc) return p;
			}
			return pool[pool.Length - 1];
		}

		class GachaDef
		{
			public string id;
			public string name;
			public int cost1;
			public int cost10;
			public GachaItemDef[] pool;
		}

		class GachaItemDef
		{
			public string code;
			public string name;
			public int rarity;
			public int weight;
		}

		[System.Serializable]
		class GachaListResponse { public GachaInfo[] gachas; }
		[System.Serializable]
		class GachaInfo { public string id; public string name; public int cost1; public int cost10; }
		[System.Serializable]
		class PullRequest { public string gachaId; public int count; }
		[System.Serializable]
		class PullResponse { public PulledItem[] items; public int money; }
		[System.Serializable]
		class PulledItem { public string code; public string name; public int rarity; }
	}
}
