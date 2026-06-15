using System.Collections.Generic;

namespace Tests.ScreenFramework.ModelBased
{
	/// <summary>
	/// 参照モデルが到達した「撹乱×ゾーン」分岐のタグ集合。スイープ全体でこのカタログを union し、
	/// 1 つでも未到達のタグがあれば「生成器がその交点を作れていない＝語彙/重みの穴」を意味する。
	/// commit hook の 5 点漏れ（2026-06-13 に手作業の引退精査で発見）のような穴を、人手のレビューでなく
	/// スイープの数値で機械検出するための土台。タグの発火点は MbtReferenceModel の各分岐。
	/// </summary>
	public static class MbtCoverage
	{
		// rollback ゾーンの停止中に来た撹乱は OCE で巻き戻る
		public const string CancelGatedLoad = "cancel.gatedLoad.oce";
		public const string CancelGatedAfterLoad = "cancel.gatedAfterLoad.oce";
		// commit ゾーンの停止中に来た外部キャンセルは無視されて完走する
		public const string CancelGatedCommitIgnored = "cancel.gatedCommit.ignored";
		public const string CancelGatedAfterEnterIgnored = "cancel.gatedAfterEnter.ignored";
		// 待機中（body 突入前）のキャンセルは自分の番で OCE
		public const string CancelWaiting = "cancel.waiting.oce";
		// 事前キャンセル済みは完全 no-op
		public const string PreCanceledNoop = "precanceled.noop";

		// preempt の犠牲者選別（rollback は殺す / commit は完走）
		public const string PreemptKillsWaiting = "preempt.kills.waiting";
		public const string PreemptKillsGatedLoad = "preempt.kills.gatedLoad";
		public const string PreemptKillsGatedAfterLoad = "preempt.kills.gatedAfterLoad";
		public const string PreemptSparesGatedCommit = "preempt.spares.gatedCommit";

		// 各ゲート境界に正常到達して解放された（撹乱なしで通過）
		public const string GateLoadReleased = "gate.load.released";
		public const string GateAfterLoadReleased = "gate.afterLoad.released";
		public const string GateCommitReleased = "gate.commit.released";
		public const string GateAfterEnterReleased = "gate.afterEnter.released";

		// rollback フォールトはスタック無傷で伝播
		public const string RollbackFaultConfigure = "rollback.fault.configure";
		public const string RollbackFaultInitialize = "rollback.fault.initialize";
		public const string RollbackFaultBeforeLoad = "rollback.fault.beforeLoad";
		public const string RollbackFaultLoad = "rollback.fault.load";
		public const string RollbackFaultAfterLoad = "rollback.fault.afterLoad";
		public const string RollbackFaultSpuriousOce = "rollback.fault.spuriousOce";

		// commit ゾーンの hook 例外は吸収されて完走
		public const string CommitHookEnterAbsorbed = "commit.hook.enter.absorbed";
		public const string CommitHookAfterEnterAbsorbed = "commit.hook.afterEnter.absorbed";

		// スタック意味論の分岐
		public const string PopToMiddleDiscard = "popto.middleDiscard";
		public const string CloseMiddle = "close.middle";
		public const string DismissAllNonEmpty = "dismissAll.nonEmpty";
		public const string DialogDelivered = "dialog.delivered";
		public const string DialogCanceled = "dialog.canceled";

		// 復元/再開
		public const string RestoreSuccess = "restore.success";
		public const string RestoreFaultDormantTop = "restore.fault.dormantTop";
		public const string ResumeSuspended = "resume.suspended";
		public const string KeepOnCoverSuspended = "keepOnCover.suspended";

		/// <summary>網羅されるべきタグの全集合。Sweep_Coverage がスイープの union と突き合わせる。</summary>
		public static readonly IReadOnlyList<string> All = new[]
		{
			CancelGatedLoad, CancelGatedAfterLoad, CancelGatedCommitIgnored, CancelGatedAfterEnterIgnored,
			CancelWaiting, PreCanceledNoop,
			PreemptKillsWaiting, PreemptKillsGatedLoad, PreemptKillsGatedAfterLoad, PreemptSparesGatedCommit,
			GateLoadReleased, GateAfterLoadReleased, GateCommitReleased, GateAfterEnterReleased,
			RollbackFaultConfigure, RollbackFaultInitialize, RollbackFaultBeforeLoad, RollbackFaultLoad,
			RollbackFaultAfterLoad, RollbackFaultSpuriousOce,
			CommitHookEnterAbsorbed, CommitHookAfterEnterAbsorbed,
			PopToMiddleDiscard, CloseMiddle, DismissAllNonEmpty, DialogDelivered, DialogCanceled,
			RestoreSuccess, RestoreFaultDormantTop, ResumeSuspended, KeepOnCoverSuspended,
		};
	}
}
