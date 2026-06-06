using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Sample.Api.Net
{
	/// <summary>
	/// アプリ全体で 1 つだけ存在する HTTP 通信の入口。
	/// <see cref="HttpRequest"/> → <see cref="UnityWebRequest"/> 変換、共通ヘッダ付与、
	/// status 分類、<see cref="ApiException"/> / <see cref="ApiTransportException"/> への詰め替え、
	/// <see cref="ApiErrorHandler"/> 連携、リトライループ を全てここに集約する。
	/// 改竄防止 hash の付与のような横断処理もここに足す方針。
	/// </summary>
	public static class HttpClient
	{
		/// <summary><see cref="HttpRequest.Path"/> に prepend する base URL。SampleBootstrap で設定。</summary>
		public static string BaseUrl;

		public static async UniTask<HttpResponse<T>> SendAsync<T>(HttpRequest msg, Options opt)
		{
			var url = BuildUrl(msg);

			// リトライループ：ApiErrorHandler が Retry を返す限り同じ内容で再送。
			// UnityWebRequest は一度送ると再利用できないのでループ内で都度 new する。
			while (true)
			{
				using var req = BuildWebRequest(url, msg);
				Exception failure = null;

				try
				{
					await req.SendWebRequest().ToUniTask(cancellationToken: opt.Ct);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (UnityWebRequestException)
				{
					failure = BuildException(req);
				}

				if (failure == null && req.result != UnityWebRequest.Result.Success)
				{
					failure = BuildException(req);
				}

				if (failure == null)
				{
					return new HttpResponse<T>
					{
						StatusCode = req.responseCode,
						Data = req.downloadHandler?.data,
						Headers = req.GetResponseHeaders(),
					};
				}

				if (opt.SuppressErrorHandling)
				{
					throw failure;
				}

				var action = await ApiErrorHandler.Handle(failure);
				if (action == ErrorAction.Retry)
				{
					continue;
				}
				throw failure;
			}
		}

		static string BuildUrl(HttpRequest msg)
		{
			var path = msg.Path ?? string.Empty;
			var baseUrl = (BaseUrl ?? string.Empty).TrimEnd('/');
			var url = baseUrl + path;
			if (msg.Query != null && msg.Query.Count > 0)
			{
				var sb = new StringBuilder(url);
				var first = true;
				foreach (var kv in msg.Query)
				{
					sb.Append(first ? '?' : '&');
					sb.Append(UnityWebRequest.EscapeURL(kv.Key));
					sb.Append('=');
					sb.Append(UnityWebRequest.EscapeURL(kv.Value));
					first = false;
				}
				url = sb.ToString();
			}
			return url;
		}

		static UnityWebRequest BuildWebRequest(string url, HttpRequest msg)
		{
			var req = new UnityWebRequest(url, ToHttpMethod(msg.Method));
			req.downloadHandler = new DownloadHandlerBuffer();

			if (msg.Body != null)
			{
				var json = JsonUtility.ToJson(msg.Body);
				req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
				req.SetRequestHeader("Content-Type", "application/json");
			}

			if (msg.Headers != null)
			{
				foreach (var kv in msg.Headers)
				{
					req.SetRequestHeader(kv.Key, kv.Value);
				}
			}

			return req;
		}

		static string ToHttpMethod(HttpMethodKind kind) => kind switch
		{
			HttpMethodKind.Get => UnityWebRequest.kHttpVerbGET,
			HttpMethodKind.Post => UnityWebRequest.kHttpVerbPOST,
			HttpMethodKind.Put => UnityWebRequest.kHttpVerbPUT,
			HttpMethodKind.Delete => UnityWebRequest.kHttpVerbDELETE,
			_ => UnityWebRequest.kHttpVerbGET,
		};

		static Exception BuildException(UnityWebRequest req)
		{
			if (req.result == UnityWebRequest.Result.ProtocolError && req.responseCode >= 400)
			{
				var raw = req.downloadHandler != null ? req.downloadHandler.text : null;
				var err = TryParseError(raw);
				return new ApiException(req.responseCode, err, raw,
					$"{req.method} {req.url} failed: {req.responseCode}");
			}

			var kind = req.result == UnityWebRequest.Result.ConnectionError
				? TransportFailure.Network
				: TransportFailure.Unknown;
			return new ApiTransportException(kind,
				$"{req.method} {req.url} transport failure ({req.result}): {req.error}");
		}

		static ErrorResponse TryParseError(string raw)
		{
			if (string.IsNullOrEmpty(raw)) return null;
			try
			{
				return JsonUtility.FromJson<ErrorResponse>(raw);
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
