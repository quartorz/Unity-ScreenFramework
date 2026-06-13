using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework.ModelBased
{
	/// <summary>
	/// モデルベース／プロパティベーステストの入口。設計と運用は docs/MODEL-BASED-TESTING.md 参照。
	///
	/// 構成:
	/// - Pinned_*: 既知の反例・代表契約の固定コーパス（決定的）。過去バグの再導入検証（ミューテーション台帳）の受け皿。
	/// - Sweep_*: シード付きランダム生成 → 参照モデルと突き合わせ → 失敗時は自動縮小して最小再現を報告。
	///
	/// Pinned_PreCanceled* の 2 件は、このハーネスが最初に検出した実バグ
	/// 「事前キャンセル済みの Preempt 操作が進行中の遷移を巻き添えキャンセルする」
	/// （ScreenNavigatorImpl.Run 冒頭の externalCt.ThrowIfCancellationRequested() 欠如、2026-06-13 修正）の番人。
	///
	/// 注入フォールトの吸収ログ（Debug.LogException）は MbtLogFilter がハンドラ層で濾すため
	/// テストを落とさない。"mbt: " を含まない予期しないエラーログは通常どおりテストを落とす。
	/// </summary>
	public sealed class ModelBasedTests
	{
		[TearDown]
		public void TearDown()
		{
			ScreenNavigator.Shutdown().Forget();   // 実行系が畳み損ねた場合の保険
		}

		// ===========================================================================
		// 固定コーパス
		// ===========================================================================

		[Test]
		public async Task Pinned_PreCanceledPreemptPush_MustNotKillInflightTransition()
		{
			// ロード中の Push に、事前キャンセル済み ct の Preempt Push を重ねる。
			// 契約: キャンセル済み操作は no-op（OCE で即決着し、in-flight 遷移は完走する）。
			// Run 冒頭ガード欠如バグ（2026-06-13 修正）の番人。ガードを外すと in-flight 側が巻き添えで死に RED になる。
			var sc = new MbtScenario();
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Push,
				Screen = new MbtScreenSpec { Uid = 1, Label = "S1" },
				Gate = MbtGateMode.HoldLoad,
			});
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Push,
				Screen = new MbtScreenSpec { Uid = 2, Label = "S2" },
				Priority = InterruptPriority.Preempt,
				Token = MbtTokenMode.PreCanceled,
				Overlap = true,
			});
			await AssertScenario(sc);
		}

		[Test]
		public async Task Pinned_PreCanceledDismissAll_MustNotKillInflightTransition()
		{
			var sc = new MbtScenario();
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Push,
				Screen = new MbtScreenSpec { Uid = 1, Label = "S1" },
				Gate = MbtGateMode.HoldLoad,
			});
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.DismissAll,
				Priority = InterruptPriority.Preempt,
				Token = MbtTokenMode.PreCanceled,
				Overlap = true,
			});
			await AssertScenario(sc);
		}

		[Test]
		public async Task Pinned_PreemptDuringLoad_LoserRollsBack_WinnerWins()
		{
			// 生きている Preempt はロード中の遷移を正当にキャンセルする（C3）。上と対になるケース。
			var sc = new MbtScenario();
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Push,
				Screen = new MbtScreenSpec { Uid = 1, Label = "S1" },
				Gate = MbtGateMode.HoldLoad,
			});
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Push,
				Screen = new MbtScreenSpec { Uid = 2, Label = "S2" },
				Priority = InterruptPriority.Preempt,
				Overlap = true,
			});
			await AssertScenario(sc);
		}

		[Test]
		public async Task Pinned_CancelInCommitZone_IsIgnored_AndPushCompletes()
		{
			// commit ゾーン（OnBeforeEnter 滞留中）の外部キャンセルは無視され完走する（C2）。
			var sc = new MbtScenario();
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Push,
				Screen = new MbtScreenSpec { Uid = 1, Label = "S1" },
				Gate = MbtGateMode.HoldCommit,
				Token = MbtTokenMode.CancelAfterIssue,
			});
			await AssertScenario(sc);
		}

		[Test]
		public async Task Pinned_PopToConfigureFault_LeavesStackIntact()
		{
			// 第 4 弾で修正したバグの番人: PopTo の Configure は中間破棄より前に評価され、
			// 例外はスタック無傷のまま伝播する（C1）。ctx 構築を破棄後に戻すと RED になる。
			var sc = new MbtScenario();
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Push, Screen = new MbtScreenSpec { Uid = 1, Label = "S1" } });
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Push, Screen = new MbtScreenSpec { Uid = 2, Label = "S2" } });
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Push, Screen = new MbtScreenSpec { Uid = 3, Label = "S3" } });
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.PopTo, TargetUid = 1, Fault = MbtOpFault.ConfigureThrows });
			await AssertScenario(sc);
		}

		[Test]
		public async Task Pinned_ResetLoadFault_ExistingStackSurvives()
		{
			// 第 1 回レビューで修正したバグの番人: Reset は「先ロード→成功後破棄」。
			// ロード失敗で既存スタックは無傷（破壊先行に戻すと RED = 黒画面復帰不能の再発検出）。
			var sc = new MbtScenario();
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Push, Screen = new MbtScreenSpec { Uid = 1, Label = "S1" } });
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Push, Screen = new MbtScreenSpec { Uid = 2, Label = "S2" } });
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Reset,
				Screen = new MbtScreenSpec { Uid = 3, Label = "S3" },
				Fault = MbtOpFault.LoadThrows,
			});
			await AssertScenario(sc);
		}

		[Test]
		public async Task Pinned_RestoreLoadFault_LeavesDormantTop_AndNavigatorRecovers()
		{
			// 復元ロード失敗は伝播するが履歴は維持され（dormant top）、以後の操作が成立する（C10 / C8）。
			var sc = new MbtScenario();
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Push,
				Screen = new MbtScreenSpec { Uid = 1, Label = "S1", Faults = MbtScreenFaults.RestoreLoadFails },
			});
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Push, Screen = new MbtScreenSpec { Uid = 2, Label = "S2" } });
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Pop });
			await AssertScenario(sc);
		}

		[Test]
		public async Task Pinned_CommitZoneHookFault_PopStillCompletes()
		{
			// 退場 hook の例外は吸収され Pop は完走する（C1 commit ゾーン契約）。
			// ExitPreviousAsync の GuardedHook を外すミューテーションで RED になる。
			var sc = new MbtScenario();
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Push, Screen = new MbtScreenSpec { Uid = 1, Label = "S1" } });
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Push,
				Screen = new MbtScreenSpec { Uid = 2, Label = "S2", Faults = MbtScreenFaults.BeforeExitThrows },
			});
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Pop });
			await AssertScenario(sc);
		}

		[Test]
		public async Task Pinned_DialogDelivery_NormalPopDelivers_SweepCancels()
		{
			// C4: 正常 Pop で結果配送、Replace による差し替えで OCE。
			// ExitPreviousAsync の isNormalPop 分岐を壊すミューテーションで RED になる。
			var sc = new MbtScenario();
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Push, Screen = new MbtScreenSpec { Uid = 1, Label = "S1" } });
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.PushAndAwait,
				Screen = new MbtScreenSpec { Uid = 2, Label = "D2", IsDialog = true, DialogResult = "R2" },
			});
			sc.Ops.Add(new MbtOp { Kind = MbtOpKind.Pop });   // D2 へ配送
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.PushAndAwait,
				Screen = new MbtScreenSpec { Uid = 3, Label = "D3", IsDialog = true, DialogResult = "R3" },
			});
			sc.Ops.Add(new MbtOp
			{
				Kind = MbtOpKind.Replace,
				Screen = new MbtScreenSpec { Uid = 4, Label = "S4" },
			});   // D3 は OCE
			await AssertScenario(sc);
		}

		// ===========================================================================
		// シードスイープ（失敗時は自動縮小して最小再現を報告する）
		// ===========================================================================

		[Test]
		public Task Sweep_Seeds_0_99() => Sweep(0, 100);

		[Test]
		public Task Sweep_Seeds_100_199() => Sweep(100, 200);

		[Test]
		public Task Sweep_Seeds_200_299() => Sweep(200, 300);

		// ===========================================================================
		// 補助
		// ===========================================================================

		static async Task AssertScenario(MbtScenario sc)
		{
			var report = await MbtExecutor.Run(sc);
			if (report.Ok) return;
			Assert.Fail($"== シナリオ ==\n{sc.Describe()}== 違反プロパティ ==\n{string.Join("\n", report.Failures)}");
		}

		static async Task Sweep(int fromSeed, int toSeedExclusive)
		{
			for (var seed = fromSeed; seed < toSeedExclusive; seed++)
			{
				var sc = MbtGenerator.Generate(seed);
				var report = await MbtExecutor.Run(sc);
				if (report.Ok) continue;

				var shrunk = await MbtShrinker.Shrink(sc);
				var shrunkReport = await MbtExecutor.Run(shrunk);
				var failures = shrunkReport.Ok ? report.Failures : shrunkReport.Failures;
				Assert.Fail(
					$"シード {seed} で反例を検出。\n" +
					$"== 縮小後の最小再現 ==\n{shrunk.Describe()}" +
					$"== 違反プロパティ ==\n{string.Join("\n", failures)}\n" +
					$"== 元のシナリオ（参考）==\n{sc.Describe()}");
			}
		}
	}
}
