using Cysharp.Threading.Tasks;
using Sample.Api.Net;

namespace Sample.Api
{
	[MockGenerator.GenerateInterface]
	public sealed partial class UserService : ApiBase
	{
		public UniTask<UserInfoResponse> Info(Options opt = default)
			=> Send<UserInfoResponse>(new HttpRequest { Path = "/user/info" }, opt);

		public UniTask<ChargeResponse> Charge(ChargeRequest req, Options opt = default)
			=> Send<ChargeResponse>(new HttpRequest
			{
				Path = "/user/charge",
				Method = HttpMethodKind.Post,
				Body = req,
			}, opt);
	}
}
