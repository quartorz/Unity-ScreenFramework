using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Sample.Api
{
	public sealed class HttpApiClient : IApiClient
	{
		readonly string _baseUrl;

		public HttpApiClient(string baseUrl)
		{
			_baseUrl = baseUrl.TrimEnd('/');
		}

		public async UniTask<UserInfoResponse> GetUserInfo(CancellationToken ct)
		{
			using var req = UnityWebRequest.Get(_baseUrl + "/user/info");
			await req.SendWebRequest().ToUniTask(cancellationToken: ct);
			ThrowIfFailed(req);
			return JsonUtility.FromJson<UserInfoResponse>(req.downloadHandler.text);
		}

		public async UniTask<ProfileResponse> GetProfile(string userId, CancellationToken ct)
		{
			var url = _baseUrl + "/profile?userId=" + UnityWebRequest.EscapeURL(userId);
			using var req = UnityWebRequest.Get(url);
			await req.SendWebRequest().ToUniTask(cancellationToken: ct);
			ThrowIfFailed(req);
			return JsonUtility.FromJson<ProfileResponse>(req.downloadHandler.text);
		}

		public async UniTask<ProfileResponse> PostProfile(ProfileRequest profile, CancellationToken ct)
		{
			var json = JsonUtility.ToJson(profile);
			using var req = new UnityWebRequest(_baseUrl + "/profile", "POST");
			req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("Content-Type", "application/json");
			await req.SendWebRequest().ToUniTask(cancellationToken: ct);
			ThrowIfFailed(req);
			return JsonUtility.FromJson<ProfileResponse>(req.downloadHandler.text);
		}

		public async UniTask<BootstrapMasterResponse> GetBootstrapMaster(CancellationToken ct)
		{
			using var req = UnityWebRequest.Get(_baseUrl + "/master/bootstrap");
			await req.SendWebRequest().ToUniTask(cancellationToken: ct);
			ThrowIfFailed(req);
			return JsonUtility.FromJson<BootstrapMasterResponse>(req.downloadHandler.text);
		}

		static void ThrowIfFailed(UnityWebRequest req)
		{
			if (req.result != UnityWebRequest.Result.Success)
			{
				throw new Exception($"{req.method} {req.url} failed: {req.responseCode} {req.error}");
			}
		}
	}
}
