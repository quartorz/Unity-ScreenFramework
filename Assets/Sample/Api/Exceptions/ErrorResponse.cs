using System;

namespace Sample.Api
{
	/// <summary>
	/// サーバーが 4xx/5xx 時に返す JSON のパース結果。
	/// 形式は <c>{ "code": "...", "message": "..." }</c> 決め打ち。
	/// </summary>
	[Serializable]
	public sealed class ErrorResponse
	{
		public string code;
		public string message;
	}
}
