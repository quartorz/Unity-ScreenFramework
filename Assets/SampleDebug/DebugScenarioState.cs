using System;
using Sample.Api;

namespace Sample.Debug
{
	/// <summary>
	/// デバッグ起動時の通信シナリオを保持する共有状態。
	/// 各 <see cref="DebugApiBase"/> 派生 Service が呼び出しごとに参照して、
	/// 擬似遅延と失敗注入を行う。操作 UI（<see cref="DebugOverlay"/> 等）は
	/// このクラスに対してだけ書き込めばよく、UI 実装とは疎結合。
	/// </summary>
	public sealed class DebugScenarioState
	{
		/// <summary>
		/// 全 Service 呼び出し共通の擬似遅延。ローディング表示が必ず見えるよう既定 300ms。
		/// </summary>
		public int DelayMs { get; set; } = 300;

		/// <summary>
		/// true のとき、次の Service 呼び出しを 1 回だけ失敗させる（消費されると false に戻る）。
		/// 失敗後にエラーダイアログでリトライを選ぶと、フラグ消費済みなので 2 回目は成功する。
		/// </summary>
		public bool FailNext { get; set; }

		/// <summary><see cref="FailNext"/> で注入する失敗の種別。</summary>
		public DebugFailureKind FailureKind { get; set; } = DebugFailureKind.Network;

		/// <summary>
		/// 失敗注入が予約されていればフラグを消費して例外を生成する。なければ null。
		/// </summary>
		public Exception TakeFailure()
		{
			if (!FailNext) return null;
			FailNext = false;
			return FailureKind switch
			{
				DebugFailureKind.ServerError => new ApiException(
					500,
					new ErrorResponse { code = "debug", message = "デバッグ用に注入されたサーバーエラーです。" },
					rawBody: null,
					message: "[Debug] injected server error"),
				DebugFailureKind.Timeout => new ApiTransportException(
					TransportFailure.Timeout, "[Debug] injected timeout"),
				_ => new ApiTransportException(
					TransportFailure.Network, "[Debug] injected network failure"),
			};
		}
	}
}
