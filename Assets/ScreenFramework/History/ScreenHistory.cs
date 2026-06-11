using System;
using System.Collections;
using System.Collections.Generic;

namespace ScreenFramework
{
	/// <summary>
	/// 履歴。最後の要素が Current（= 現在表示中）。
	/// </summary>
	internal sealed class ScreenHistory : IScreenHistory
	{
		readonly List<IScreenIdentifier> _stack = new();
		readonly object _lock = new();

		public IScreenIdentifier Current
			=> _stack.Count == 0 ? null : _stack[_stack.Count - 1];

		public int Count => _stack.Count;
		public IScreenIdentifier this[int index] => _stack[index];

		public IEnumerator<IScreenIdentifier> GetEnumerator() => _stack.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => _stack.GetEnumerator();

		// ---- 演出ありの操作（Navigator のみが呼ぶ）----

		internal void Push(IScreenIdentifier id) => _stack.Add(id);

		internal IScreenIdentifier PopCurrent()
		{
			if (_stack.Count == 0) return null;
			var top = _stack[_stack.Count - 1];
			_stack.RemoveAt(_stack.Count - 1);
			return top;
		}

		internal void ReplaceCurrent(IScreenIdentifier id)
		{
			if (_stack.Count == 0) _stack.Add(id);
			else _stack[_stack.Count - 1] = id;
		}

		internal void RemoveAtInternal(int index) => _stack.RemoveAt(index);

		internal void ClearAll() => _stack.Clear();

		/// <summary>
		/// Current を残して下を全部消す。Navigator の複合操作（Change）用。
		/// 並走する LiveEntry 側の同期は呼び出し側の責任。
		/// </summary>
		internal void ClearBelow()
		{
			if (_stack.Count <= 1) return;
			var current = _stack[_stack.Count - 1];
			_stack.Clear();
			_stack.Add(current);
		}

		/// <summary>
		/// Current より下を <paramref name="below"/> で置き換える。
		/// <see cref="EditOverride"/> 経由の同期編集（Navigator 側）から呼ばれる。
		/// </summary>
		internal void RebuildBelow(IReadOnlyList<IScreenIdentifier> below)
		{
			if (_stack.Count == 0) return;
			var current = _stack[_stack.Count - 1];
			_stack.Clear();
			for (var i = 0; i < below.Count; i++) _stack.Add(below[i]);
			_stack.Add(current);
		}

		// ---- 無音編集 ----

		/// <summary>
		/// 設定されている場合、<see cref="Edit"/> はこの delegate に委譲される。
		/// Navigator が履歴と並走する LiveEntry リストを同期編集するために差し込む。
		/// </summary>
		internal Action<Action<IScreenHistoryEditor>> EditOverride;

		public void Edit(Action<IScreenHistoryEditor> action)
		{
			if (action == null) throw new ArgumentNullException(nameof(action));
			if (EditOverride != null)
			{
				EditOverride(action);
				return;
			}
			lock (_lock)
			{
				if (_stack.Count == 0)
				{
					action(new Editor(new List<IScreenIdentifier>(), 0));
					return;
				}
				var current = _stack[_stack.Count - 1];
				var below = _stack.GetRange(0, _stack.Count - 1);
				var editor = new Editor(below, 0);
				action(editor);
				_stack.Clear();
				_stack.AddRange(editor.Stack);
				_stack.Add(current);
			}
		}

		sealed class Editor : IScreenHistoryEditor
		{
			public IList<IScreenIdentifier> Stack { get; }
			public Editor(IList<IScreenIdentifier> stack, int _) { Stack = stack; }
			public void Clear() => Stack.Clear();
			public void RemoveAt(int index) => Stack.RemoveAt(index);
			public void RemoveAll(Predicate<IScreenIdentifier> match)
			{
				for (var i = Stack.Count - 1; i >= 0; i--)
				{
					if (match(Stack[i])) Stack.RemoveAt(i);
				}
			}
			public void Insert(int index, IScreenIdentifier id) => Stack.Insert(index, id);
		}
	}
}
