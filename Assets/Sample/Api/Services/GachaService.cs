using Cysharp.Threading.Tasks;
using Sample.Api.Net;

namespace Sample.Api
{
	[MockGenerator.GenerateInterface]
	public sealed partial class GachaService : ApiBase
	{
		public UniTask<GachaListResponse> List(Options opt = default)
			=> Send<GachaListResponse>(new HttpRequest { Path = "/gacha/list" }, opt);

		public UniTask<GachaPullResponse> Pull(GachaPullRequest req, Options opt = default)
			=> Send<GachaPullResponse>(new HttpRequest
			{
				Path = "/gacha/pull",
				Method = HttpMethodKind.Post,
				Body = req,
			}, opt);
	}
}
