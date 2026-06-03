using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// 演出なし。瞬間的に切り替える。
	/// </summary>
	public sealed class ImmediateTransition : IScreenTransitionDirector
	{
		public static readonly ImmediateTransition Instance = new();
		public IScreenTransitionHandle CreateHandle() => Handle.Singleton;

		sealed class Handle : IScreenTransitionHandle
		{
			public static readonly Handle Singleton = new();
			public UniTask Start(CancellationToken ct) => UniTask.CompletedTask;
			public UniTask End(CancellationToken ct) => UniTask.CompletedTask;
		}
	}
}
