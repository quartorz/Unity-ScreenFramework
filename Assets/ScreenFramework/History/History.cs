using System;
using System.Collections.Generic;

namespace ScreenFramework
{
	public interface IScreenHistory : IReadOnlyList<IScreenIdentifier>
	{
		IScreenIdentifier Current { get; }
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
