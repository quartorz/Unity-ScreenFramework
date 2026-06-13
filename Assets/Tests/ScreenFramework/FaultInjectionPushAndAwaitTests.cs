using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// フォールトインジェクションテスト: PushAndAwait の決着保証のうち、<b>History.Edit 経由の破棄</b>のみ。
	/// 「正常 Pop / Close で配送、preempt / Replace / Change / Reset / DismissAll / PopTo 中間破棄 / 外部キャンセル /
	/// ロード失敗で OCE」という決着保証の大半は、モデルベーステスト（<c>ModelBased/</c>）の P2/P4 が
	/// 操作×撹乱の直積として網羅するため引退した（2026-06-13。docs/MODEL-BASED-TESTING.md の引退節参照）。
	/// History.Edit は MBT の語彙外のためここに残す。
	/// </summary>
	public sealed class FaultInjectionPushAndAwaitTests : FaultInjectionTestBase
	{
		[Test]
		public async Task PushAndAwait_SweptByHistoryEdit_AwaiterGetsOce()
		{
			// History.Edit で履歴から外された行は Exit hook なしの teardown(CleanupDetachedEntry)に入り、
			// 待機中の awaiter は OCE で決着する(ハングしない)。
			SetupNavigator(ScreenCacheMode.KeepOnCover);   // dialog を生きたまま中間に埋める
			await ScreenNavigator.Page.Push(new MarkerScreenId("Base"));
			var resultTask = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("never"));
			var idTop = new MarkerScreenId("Top");
			await ScreenNavigator.Page.Push(idTop);

			ScreenNavigator.Page.History.Edit(e => e.RemoveAt(1));   // dialog の行を外す

			Exception caught = null;
			try { await resultTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "Edit で外された dialog の awaiter は OCE で決着する");
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idTop, ScreenNavigator.Page.Current);
		}
	}
}
