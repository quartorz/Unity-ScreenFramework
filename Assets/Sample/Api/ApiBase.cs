using Cysharp.Threading.Tasks;
using Sample.Api.Net;

namespace Sample.Api
{
	/// <summary>
	/// 各 Service の共通基底。
	/// <see cref="HttpClient"/> を呼んでデシリアライズ済みレスポンスを返す薄い helper を持つだけ。
	/// </summary>
	public abstract class ApiBase
	{
		protected static async UniTask<TResp> Send<TResp>(HttpRequest req, Options opt)
		{
			var resp = await HttpClient.SendAsync<TResp>(req, opt);
			return resp.GetData();
		}
	}
}
