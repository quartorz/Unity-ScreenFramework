using System.Collections.Generic;

namespace Sample.Api.Net
{
	/// <summary>
	/// アプリ側がサーバーに送りたいリクエストの中立な表現。
	/// <see cref="HttpClient"/> 内で <see cref="UnityEngine.Networking.UnityWebRequest"/> に変換される。
	/// 改竄防止ヘッダ等の横断処理は HttpClient 側で付与する。
	/// </summary>
	public sealed class HttpRequest
	{
		public string Path;
		public HttpMethodKind Method = HttpMethodKind.Get;

		/// <summary>JSON body にしたいオブジェクト。null なら body なし。</summary>
		public object Body;

		public Dictionary<string, string> Headers;
		public Dictionary<string, string> Query;
	}
}
