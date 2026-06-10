using System;
using Cysharp.Threading.Tasks;
using Sample.Api;
using Sample.Api.Net;

namespace Sample.Debug
{
	/// <summary>
	/// デバッグ用 Service の共通基底。本物の <see cref="Sample.Api.Net.HttpClient"/> が担っている
	/// 「遅延・例外分類・<see cref="ApiErrorHandler"/> 連携・リトライループ」をローカルで模倣する。
	/// 各 Service は <see cref="Send{TResp}"/> にレスポンス生成関数を渡すだけでよい。
	/// </summary>
	public abstract class DebugApiBase
	{
		protected UserDataHolder UserData { get; }
		protected DebugScenarioState Scenario { get; }

		protected DebugApiBase(UserDataHolder userData, DebugScenarioState scenario)
		{
			UserData = userData;
			Scenario = scenario;
		}

		/// <summary>
		/// 擬似遅延 → 失敗注入チェック → レスポンス生成、の順で本物の通信 1 回分を模倣する。
		/// 失敗時は本物同様 <see cref="ApiErrorHandler"/> に渡し、Retry が返る限りループする
		/// （注入失敗は 1 回で消費されるのでリトライは成功する）。
		/// <paramref name="make"/> が <see cref="ApiException"/> を投げた場合（残高不足等の業務エラー）も
		/// 同じ経路でダイアログ表示される。
		/// </summary>
		protected async UniTask<TResp> Send<TResp>(Func<TResp> make, Options opt)
		{
			while (true)
			{
				if (Scenario.DelayMs > 0)
				{
					await UniTask.Delay(Scenario.DelayMs, cancellationToken: opt.Ct);
				}

				var failure = Scenario.TakeFailure();
				if (failure == null)
				{
					try
					{
						return make();
					}
					catch (ApiException e)
					{
						failure = e;
					}
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
	}
}
