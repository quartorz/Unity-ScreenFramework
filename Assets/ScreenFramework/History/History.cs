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
