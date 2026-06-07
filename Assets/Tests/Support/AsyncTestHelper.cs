using System;
using Cysharp.Threading.Tasks;

namespace Tests
{
	/// <summary>
	/// テスト用の非同期ユーティリティ。Mock 関数を <c>UniTask.FromResult</c> で書くと
	/// 同期完了する mock になり「同期前提の検証」を許してしまうため、
	/// 1 フレーム分の await を必ず挟んで返す/投げるヘルパを提供する。
	/// </summary>
	public static class AsyncTestHelper
	{
		/// <summary><paramref name="value"/> を 1 フレーム後に返す <see cref="UniTask{T}"/>。</summary>
		public static async UniTask<T> Return<T>(T value)
		{
			await UniTask.Yield();
			return value;
		}

		/// <summary>1 フレーム後に完了する <see cref="UniTask"/>。</summary>
		public static async UniTask Done()
		{
			await UniTask.Yield();
		}

		/// <summary>1 フレーム後に <paramref name="exception"/> を投げる <see cref="UniTask{T}"/>。</summary>
		public static async UniTask<T> Throw<T>(Exception exception)
		{
			await UniTask.Yield();
			throw exception;
		}

		/// <summary>1 フレーム後に <paramref name="exception"/> を投げる <see cref="UniTask"/>。</summary>
		public static async UniTask Throw(Exception exception)
		{
			await UniTask.Yield();
			throw exception;
		}
	}
}
