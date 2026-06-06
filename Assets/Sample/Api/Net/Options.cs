using System.Threading;

namespace Sample.Api.Net
{
	/// <summary>
	/// 通信オプション。
	/// <see cref="Ct"/> は呼び出し側のキャンセル token、
	/// <see cref="SuppressErrorHandling"/> が true なら <see cref="ApiErrorHandler"/>
	/// による SystemDialog 表示をスキップしてそのまま例外を投げる。
	/// </summary>
	public struct Options
	{
		public CancellationToken Ct;
		public bool SuppressErrorHandling;

		public Options(CancellationToken ct, bool suppressErrorHandling = false)
		{
			Ct = ct;
			SuppressErrorHandling = suppressErrorHandling;
		}
	}
}
