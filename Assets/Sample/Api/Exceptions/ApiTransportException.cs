using System;

namespace Sample.Api
{
	public enum TransportFailure
	{
		Unknown,
		Network,
		Timeout,
	}

	/// <summary>
	/// サーバーまで到達しなかった通信失敗（ネットワーク・タイムアウト等）の例外。
	/// サーバーが返した HTTP ステータスがある場合は <see cref="ApiException"/> を使う。
	/// </summary>
	public sealed class ApiTransportException : Exception
	{
		public TransportFailure Kind { get; }

		public ApiTransportException(TransportFailure kind, string message)
			: base(message)
		{
			Kind = kind;
		}
	}
}
