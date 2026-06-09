using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// <see cref="ITransitionContext"/> の内部実装。Navigator が遷移ごとに 1 個作って Presenter/Effect に渡す。
	/// stage signal は <c>Type → UniTaskCompletionSource</c> の dict で保持し、publish 済みフラグも兼ねる。
	/// </summary>
	internal sealed class TransitionContext : ITransitionContext
	{
		readonly Dictionary<Type, UniTaskCompletionSource> _stages = new();

		public OperationKind Kind { get; }
		public IScreenIdentifier From { get; }
		public IScreenIdentifier To { get; }
		public INavigationDataReader Reader { get; }
		public INavigationDataWriter Writer { get; }

		public TransitionContext(
			OperationKind kind,
			IScreenIdentifier from,
			IScreenIdentifier to,
			INavigationDataReader reader,
			INavigationDataWriter writer)
		{
			Kind = kind;
			From = from;
			To = to;
			Reader = reader;
			Writer = writer;
		}

		public void PublishStage<TStage>() where TStage : IStageKey
		{
			var src = GetOrCreateSource(typeof(TStage));
			src.TrySetResult();
		}

		public async UniTask WaitForStage<TStage>(CancellationToken ct = default, TimeSpan? timeout = null) where TStage : IStageKey
		{
			var src = GetOrCreateSource(typeof(TStage));
			if (src.Task.Status == UniTaskStatus.Succeeded)
			{
				return;
			}

			if (timeout.HasValue)
			{
				await src.Task.AttachExternalCancellation(ct).Timeout(timeout.Value);
			}
			else
			{
				await src.Task.AttachExternalCancellation(ct);
			}
		}

		UniTaskCompletionSource GetOrCreateSource(Type key)
		{
			if (!_stages.TryGetValue(key, out var src))
			{
				src = new UniTaskCompletionSource();
				_stages[key] = src;
			}
			return src;
		}
	}
}
