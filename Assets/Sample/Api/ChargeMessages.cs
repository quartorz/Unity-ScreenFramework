using System;

namespace Sample.Api
{
	[Serializable]
	public class ChargeRequest
	{
		public int amount;
	}

	[Serializable]
	public class ChargeResponse
	{
		public int money;
	}
}
