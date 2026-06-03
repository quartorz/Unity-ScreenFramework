using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sample.Api
{
	public interface IApiClient
	{
		/// <summary>起動時の自ユーザーデータ一括取得 (/user/info)。</summary>
		UniTask<UserInfoResponse> GetUserInfo(CancellationToken ct);

		/// <summary>指定 userId のプロフィールを取得 (/profile?userId=xxx)。</summary>
		UniTask<ProfileResponse> GetProfile(string userId, CancellationToken ct);

		UniTask<ProfileResponse> PostProfile(ProfileRequest profile, CancellationToken ct);

		UniTask<BootstrapMasterResponse> GetBootstrapMaster(CancellationToken ct);

		UniTask<GachaListResponse> GetGachaList(CancellationToken ct);

		UniTask<GachaPullResponse> PullGacha(GachaPullRequest request, CancellationToken ct);

		/// <summary>課金を模した所持金加算 (/user/charge)。</summary>
		UniTask<ChargeResponse> ChargeMoney(ChargeRequest request, CancellationToken ct);
	}
}
