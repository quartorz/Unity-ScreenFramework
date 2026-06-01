using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	public interface IScreenTransitionDirector
	{
		IScreenTransitionHandle CreateHandle();
	}

	public interface IScreenTransitionHandle
	{
		UniTask Start(CancellationToken ct);
		UniTask End(CancellationToken ct);
	}
}
