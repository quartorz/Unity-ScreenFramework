using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// フォールトインジェクションテスト: 外部キャンセルと割り込み(Preempt / Queue)のうち、<b>Stage 待ちの
	/// 外部キャンセル</b>のみ。外部 ct は rollback ゾーンでのみ有効 / commit ゾーンと Pop 系では無視 / preempt は
	/// rollback ゾーンの遷移だけ殺す / Queue は完走待ち、といった設計原則の大半は、モデルベーステスト
	/// （<c>ModelBased/</c>）が token × gate × priority × overlap の直積として網羅するため引退した
	/// （2026-06-13。docs/MODEL-BASED-TESTING.md の引退節参照）。
	/// WaitForStage（Stage 機構）は MBT の語彙外のためここに残す。
	/// </summary>
	public sealed class FaultInjectionCancelInterruptTests : FaultInjectionTestBase
	{
		[Test]
		public async Task Push_WaitForStageCanceledExternally_RollsBackWithOce()
		{
			// timeout なしの WaitForStage が外部キャンセルで OCE 決着し、遷移は補償付きで巻き戻る。
			SetupNavigator();
			using var cts = new CancellationTokenSource();
			var handle = new InstantHandle();
			var presenter = new StageWaitCancelPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			var pushTask = ScreenNavigator.Page.Push(id, ct: cts.Token);
			cts.Cancel();

			Exception caught = null;
			try { await pushTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "stage 待ちの外部キャンセルは OCE で決着する(ハングしない)");
			Assert.IsTrue(handle.UnloadCalled, "キャンセルされた遷移も補償 Unload される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "破棄経路でも OnAfterUnload が呼ばれる");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "キャンセル後も次の Push が成立する");
		}
	}
}
