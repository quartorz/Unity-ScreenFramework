using System;

namespace Sample.Api
{
	[Serializable]
	public class GachaListResponse
	{
		public GachaInfoResponse[] gachas;
	}

	[Serializable]
	public class GachaInfoResponse
	{
		public string id;
		public string name;
		public int cost1;
		public int cost10;
	}

	[Serializable]
	public class GachaPullRequest
	{
		public string gachaId;
		public int count;
	}

	[Serializable]
	public class GachaPullResponse
	{
		public PulledItemResponse[] items;
		public int money;
	}

	[Serializable]
	public class PulledItemResponse
	{
		public string code;
		public string name;
		public int rarity;
	}
}
