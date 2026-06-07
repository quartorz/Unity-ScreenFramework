using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Sample.Api.Net;

namespace Sample.Api
{
	[MockGenerator.GenerateInterface]
	public sealed partial class ProfileService : ApiBase
	{
		public ProfileService(UserDataHolder userData) : base(userData) { }

		public UniTask<ProfileResponse> Get(string userId, Options opt = default)
			=> Send<ProfileResponse>(new HttpRequest
			{
				Path = "/profile",
				Query = new Dictionary<string, string> { ["userId"] = userId },
			}, opt);

		public UniTask<ProfileResponse> Post(ProfileRequest req, Options opt = default)
			=> Send<ProfileResponse>(new HttpRequest
			{
				Path = "/profile",
				Method = HttpMethodKind.Post,
				Body = req,
			}, opt);
	}
}
