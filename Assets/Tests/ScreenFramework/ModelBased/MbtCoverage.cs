using System.Collections.Generic;

namespace Tests.ScreenFramework.ModelBased
{
	/// <summary>
	/// 参照モデルが到達した「撹乱×ゾーン」分岐のタグ集合。Sweep_Coverage がスイープ全体でこのカタログを
	/// union し、未到達のタグがあれば生成器がその交点を作れていない（語彙/重みの穴）ことを表す。
	/// タグの発火点は MbtReferenceModel の各分岐。
	/// </summary>
	public static class MbtCoverage
	{
		// rollback ゾーンの停止中に来た撹乱は OCE で巻き戻る
		public const string CancelGatedInitialize = "cancel.gatedInitialize.oce";
		public const string CancelGatedLoad = "cancel.gatedLoad.oce";
		public const string CancelGatedAfterLoad = "cancel.gatedAfterLoad.oce";
		// commit ゾーンの停止中に来た外部キャンセルは無視されて完走する
		public const string CancelGatedCommitIgnored = "cancel.gatedCommit.ignored";
		public const string CancelGatedAfterShowIgnored = "cancel.gatedAfterShow.ignored";
		// 退場 hook（Pop の OnBeforeHide）滞留中の外部キャンセルも commit ゾーンなので無視される
		public const string CancelGatedExitIgnored = "cancel.gatedExit.ignored";
		// 待機中（body 突入前）のキャンセルは自分の番で OCE
		public const string CancelWaiting = "cancel.waiting.oce";
		// 事前キャンセル済みは完全 no-op
		public const string PreCanceledNoop = "precanceled.noop";

		// preempt の犠牲者選別（rollback は殺す / commit は完走）
		public const string PreemptKillsWaiting = "preempt.kills.waiting";
		public const string PreemptKillsGatedInitialize = "preempt.kills.gatedInitialize";
		public const string PreemptKillsGatedLoad = "preempt.kills.gatedLoad";
		public const string PreemptKillsGatedAfterLoad = "preempt.kills.gatedAfterLoad";
		public const string PreemptSparesGatedCommit = "preempt.spares.gatedCommit";
		public const string PreemptSparesGatedExit = "preempt.spares.gatedExit";

		// 各ゲート境界に正常到達して解放された（撹乱なしで通過）
		public const string GateInitializeReleased = "gate.initialize.released";
		public const string GateLoadReleased = "gate.load.released";
		public const string GateAfterLoadReleased = "gate.afterLoad.released";
		public const string GateCommitReleased = "gate.commit.released";
		public const string GateAfterShowReleased = "gate.afterShow.released";
		public const string GateExitReleased = "gate.exit.released";

		// rollback フォールトはスタック無傷で伝播
		public const string RollbackFaultConfigure = "rollback.fault.configure";
		public const string RollbackFaultInitialize = "rollback.fault.initialize";
		public const string RollbackFaultBeforeLoad = "rollback.fault.beforeLoad";
		public const string RollbackFaultLoad = "rollback.fault.load";
		public const string RollbackFaultAfterLoad = "rollback.fault.afterLoad";
		public const string RollbackFaultSpuriousOce = "rollback.fault.spuriousOce";

		// commit ゾーンの hook 例外は吸収されて完走
		public const string CommitHookEnterAbsorbed = "commit.hook.enter.absorbed";
		public const string CommitHookAfterShowAbsorbed = "commit.hook.afterShow.absorbed";

		// スタック意味論の分岐
		public const string PopToMiddleDiscard = "popto.middleDiscard";
		public const string CloseMiddle = "close.middle";
		public const string DismissAllNonEmpty = "dismissAll.nonEmpty";
		public const string DialogDelivered = "dialog.delivered";
		public const string DialogCanceled = "dialog.canceled";

		// 復元/再開
		public const string RestoreSuccess = "restore.success";
		public const string RestoreFaultDormantTop = "restore.fault.dormantTop";   // Pop キャンセル（復元ロード失敗）
		public const string ResumeFaultCancelsPop = "resume.fault.cancelsPop";   // Pop キャンセル（OnResume 失敗）
		public const string ResumeSuspended = "resume.suspended";
		public const string KeepOnCoverSuspended = "keepOnCover.suspended";

		// Stack モード（覆っても下画面を残す）/ Shutdown 途中差し
		public const string StackCoverNoExit = "stack.cover.noExit";
		public const string StackBlockerCreated = "stack.blocker.created";
		public const string ShutdownFold = "shutdown.fold";

		// History.Edit（Current より下の行の無音編集）
		public const string EditImmediate = "edit.immediate";
		public const string EditDeferred = "edit.deferred";
		public const string EditEmptyNoop = "edit.emptyNoop";
		public const string EditRemoveAt = "edit.removeAt";
		public const string EditRemoveByUid = "edit.removeByUid";
		public const string EditInsert = "edit.insert";
		public const string EditReplaceAt = "edit.replaceAt";
		public const string EditClear = "edit.clear";
		public const string EditRemovedLiveEntry = "edit.removedLiveEntry";

		/// <summary>網羅されるべきタグの全集合。Sweep_Coverage がスイープの union と突き合わせる。</summary>
		public static readonly IReadOnlyList<string> All = new[]
		{
			CancelGatedInitialize, CancelGatedLoad, CancelGatedAfterLoad, CancelGatedCommitIgnored, CancelGatedAfterShowIgnored,
			CancelGatedExitIgnored, CancelWaiting, PreCanceledNoop,
			PreemptKillsWaiting, PreemptKillsGatedInitialize, PreemptKillsGatedLoad, PreemptKillsGatedAfterLoad,
			PreemptSparesGatedCommit, PreemptSparesGatedExit,
			GateInitializeReleased, GateLoadReleased, GateAfterLoadReleased, GateCommitReleased, GateAfterShowReleased, GateExitReleased,
			RollbackFaultConfigure, RollbackFaultInitialize, RollbackFaultBeforeLoad, RollbackFaultLoad,
			RollbackFaultAfterLoad, RollbackFaultSpuriousOce,
			CommitHookEnterAbsorbed, CommitHookAfterShowAbsorbed,
			PopToMiddleDiscard, CloseMiddle, DismissAllNonEmpty, DialogDelivered, DialogCanceled,
			StackCoverNoExit, StackBlockerCreated, ShutdownFold,
			RestoreSuccess, RestoreFaultDormantTop, ResumeFaultCancelsPop, ResumeSuspended, KeepOnCoverSuspended,
			EditImmediate, EditDeferred, EditEmptyNoop, EditRemoveAt, EditRemoveByUid,
			EditInsert, EditReplaceAt, EditClear, EditRemovedLiveEntry,
		};
	}
}
