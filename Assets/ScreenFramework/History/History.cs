using System;
using System.Collections.Generic;

namespace ScreenFramework
{
	public interface IScreenHistory : IReadOnlyList<IScreenIdentifier>
	{
		IScreenIdentifier Current { get; }

		/// <summary>
		/// 履歴を演出なしで無音編集する。編集対象は Current より下の行のみで、Current は維持される。
		/// 履歴と並走する画面インスタンス（<c>KeepOnCover</c> や Stack モードで保持されているもの）も
		/// 同期して編集され、編集で履歴から外れた行に生きたインスタンスがあった場合は
		/// Exit 演出・Exit hook なしで即 Unload される（<c>OnAfterUnload</c> のみ呼ばれ、
		/// <c>PushAndAwait</c> の待機者にはキャンセルが通知される）。
		/// 挿入した行は dormant（インスタンスなし）として入り、Pop で到達した時にロードされる。
		/// 履歴が空のときは編集は適用されない。
		/// <para>
		/// <b>遷移中の呼び出し</b>: 遷移実行中（<see cref="IScreenNavigator.IsTransitioning"/> が true）に呼ぶと、
		/// その遷移と連鎖する preempt / queue が全て完了した後にまとめて適用される。
		/// 遷移の途中で履歴の並走リストを書き換えると、進行中の操作が掴んでいる index が無効化されるため。
		/// </para>
		/// <para>
		/// <b>callback 内から遷移 API を呼ばないこと</b>: callback の実行中にスタックが動くと
		/// 編集前に取ったスナップショットが古くなるため、その編集はエラーログとともに破棄される。
		/// 編集と遷移を両方行いたい場合は、Edit を完了させてから遷移を発行する。
		/// </para>
		/// </summary>
		void Edit(Action<IScreenHistoryEditor> action);
	}

	public interface IScreenHistoryEditor
	{
		IList<IScreenIdentifier> Stack { get; }
		void Clear();
		void RemoveAt(int index);
		void RemoveAll(Predicate<IScreenIdentifier> match);
		void Insert(int index, IScreenIdentifier id);
	}
}
