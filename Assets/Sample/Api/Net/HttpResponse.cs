using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sample.Api.Net
{
	public class HttpResponse
	{
		public long StatusCode;
		public byte[] Data;
		public Dictionary<string, string> Headers;
	}

	public sealed class HttpResponse<T> : HttpResponse
	{
		public T GetData()
		{
			if (Data == null || Data.Length == 0) return default;
			var text = Encoding.UTF8.GetString(Data);
			return JsonUtility.FromJson<T>(text);
		}
	}
}
