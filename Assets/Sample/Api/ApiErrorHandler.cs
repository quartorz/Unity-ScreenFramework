using System;
using Cysharp.Threading.Tasks;
using Sample.Dialogs;
using ScreenFramework;
using UnityEngine;

namespace Sample.Api
{
	public enum ErrorAction
	{
		Cancel,
		Retry,
	}

	/// <summary>
	/// 通信エラーを SystemDialog で 1 箇所からまとめて表示する static handler。
	/// <see cref="Net.HttpClient.SendAsync{T}"/> から <see cref="Options.SuppressErrorHandling"/> が
	/// false のときだけ呼ばれる。
	/// <para>
	/// 戻り値の <see cref="ErrorAction"/> が <c>Retry</c> なら HttpClient 側で同じリクエストを再送し、
	/// <c>Cancel</c> なら例外を呼び出し側に投げる。
	/// </para>
	/// <para>
	/// 通信失敗（電波・タイムアウト等 <see cref="ApiTransportException"/>）は復帰見込みありで
	/// 「リトライ / 閉じる」二択。サーバーエラー（<see cref="ApiException"/>）はリトライで直らないので OK のみ。
	/// </para>
	/// </summary>
	public static class ApiErrorHandler
	{
		public static async UniTask<ErrorAction> Handle(Exception ex)
		{
			var (title, message, retryable) = Format(ex);
			Debug.LogError($"[ApiErrorHandler] {title}: {message}\n{ex}");

			if (retryable)
			{
				// 「閉じる」=0 / 「リトライ」=1。閉じる(X) は無効（誤タップで復帰機会を失わないように）。
				var result = await ScreenNavigator.SystemDialog.PushAndAwait(
					new MessageDialogId(title, message, new[] { "閉じる", "リトライ" }, ShowCloseButton: false));
				return (result != null && result.Index == 1) ? ErrorAction.Retry : ErrorAction.Cancel;
			}
			else
			{
				await ScreenNavigator.SystemDialog.PushAndAwait(
					new MessageDialogId(title, message, new[] { "OK" }, ShowCloseButton: false));
				return ErrorAction.Cancel;
			}
		}

		static (string title, string message, bool retryable) Format(Exception ex)
		{
			switch (ex)
			{
				case ApiException api:
				{
					var msg = api.Error != null && !string.IsNullOrEmpty(api.Error.message)
						? api.Error.message
						: $"サーバーエラー ({api.StatusCode})";
					return ("通信エラー", msg, false);
				}
				case ApiTransportException t when t.Kind == TransportFailure.Network:
					return ("通信エラー", "ネットワークに接続できませんでした。", true);
				case ApiTransportException t when t.Kind == TransportFailure.Timeout:
					return ("通信エラー", "通信がタイムアウトしました。", true);
				case ApiTransportException _:
					return ("通信エラー", "通信に失敗しました。", true);
				default:
					return ("通信エラー", ex.Message, false);
			}
		}
	}
}
