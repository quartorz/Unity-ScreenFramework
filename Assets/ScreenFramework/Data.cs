using System;
using System.Collections.Generic;

namespace ScreenFramework
{
	public interface IScreenData { }

	public interface IScreenDataReader
	{
		bool TryRead<T>(out T data) where T : IScreenData;
	}

	public interface IScreenDataWriter
	{
		void Write<T>(T data) where T : IScreenData;
	}

	internal sealed class ScreenDataStore : IScreenDataReader, IScreenDataWriter
	{
		readonly Dictionary<Type, IScreenData> _data = new();

		public bool TryRead<T>(out T data) where T : IScreenData
		{
			if (_data.TryGetValue(typeof(T), out var value))
			{
				data = (T)value;
				return true;
			}
			data = default;
			return false;
		}

		public void Write<T>(T data) where T : IScreenData
		{
			_data[typeof(T)] = data;
		}

		/// <summary>
		/// 実行時の型でキー付けして書き込む（PushOptions.Data などの boxed 経路向け）。
		/// </summary>
		public void WriteUntyped(IScreenData data)
		{
			if (data == null) return;
			_data[data.GetType()] = data;
		}

		public void Clear() => _data.Clear();
	}

	internal sealed class EmptyScreenDataReader : IScreenDataReader
	{
		public static readonly EmptyScreenDataReader Instance = new();
		public bool TryRead<T>(out T data) where T : IScreenData
		{
			data = default;
			return false;
		}
	}
}
