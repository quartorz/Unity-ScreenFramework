namespace Sample.Debug
{
	/// <summary>
	/// <see cref="DebugScenarioState"/> で注入する失敗の種別。
	/// 本物の通信経路が投げる例外の分類（<see cref="Sample.Api.ApiException"/> /
	/// <see cref="Sample.Api.ApiTransportException"/>）をミラーしている。
	/// </summary>
	public enum DebugFailureKind
	{
		/// <summary>HTTP 500 相当。リトライ不可、OK のみのダイアログになる。</summary>
		ServerError,

		/// <summary>ネットワーク到達失敗相当。リトライ可のダイアログになる。</summary>
		Network,

		/// <summary>タイムアウト相当。リトライ可のダイアログになる。</summary>
		Timeout,
	}
}
