using System;
using System.Collections.Generic;

namespace ScreenFramework
{
	public interface INavigationData { }

	public interface INavigationDataReader
	{
		bool TryRead<T>(out T data) where T : INavigationData;
	}

	public interface INavigationDataWriter
	{
		void Write<T>(T data) where T : INavigationData;
	}

	internal sealed class NavigationDataStore : INavigationDataReader, INavigationDataWriter
	{
		readonly Dictionary<Type, INavigationData> _data = new();

		public bool TryRead<T>(out T data) where T : INavigationData
		{
			if (_data.TryGetValue(typeof(T), out var value))
			{
				data = (T)value;
				return true;
			}
			data = default;
			return false;
		}

		public void Write<T>(T data) where T : INavigationData
		{
			_data[typeof(T)] = data;
		}

		/// <summary>
		/// 実行時の型でキー付けして書き込む（PushOptions.Data などの boxed 経路向け）。
		/// </summary>
		public void WriteUntyped(INavigationData data)
		{
			if (data == null) return;
			_data[data.GetType()] = data;
		}

		public void Clear() => _data.Clear();
	}

	internal sealed class EmptyNavigationDataReader : INavigationDataReader
	{
		public static readonly EmptyNavigationDataReader Instance = new();
		public bool TryRead<T>(out T data) where T : INavigationData
		{
			data = default;
			return false;
		}
	}
}
