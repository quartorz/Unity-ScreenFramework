using System;

namespace Sample.Api
{
	[Serializable]
	public class ProfileResponse
	{
		public string userId;
		public string name;
		public int level;
	}

	[Serializable]
	public class ProfileRequest
	{
		public string userId;
		public string name;
		public int level;
	}
}
