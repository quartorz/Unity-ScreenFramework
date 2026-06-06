using System;

namespace Sample.Api
{
	/// <summary>
	/// サーバーが HTTP 4xx/5xx を返したときの例外。
	/// レスポンスボディが <see cref="ErrorResponse"/> 形式でパースできた場合は <see cref="Error"/> に詰める。
	/// 任意のサーバー独自フィールドを後から拾いたい場合のため <see cref="RawBody"/> も保持しておく。
	/// </summary>
	public sealed class ApiException : Exception
	{
		public long StatusCode { get; }
		public ErrorResponse Error { get; }
		public string RawBody { get; }

		public ApiException(long statusCode, ErrorResponse error, string rawBody, string message)
			: base(message)
		{
			StatusCode = statusCode;
			Error = error;
			RawBody = rawBody;
		}
	}
}
