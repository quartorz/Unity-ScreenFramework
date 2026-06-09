using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine.TestTools;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// <see cref="ITransitionContext"/> の stage signal: sticky publish、複数 waiter への broadcast、
	/// publish 前の waiter が正しく resume すること。
	/// </summary>
	public sealed class StageSignalTests
	{
		sealed class StageA : IStageKey { }
		sealed class StageB : IStageKey { }

		static ITransitionContext NewCtx()
		{
			var store = new InternalNavigationDataStore();
			return new TransitionContext(OperationKind.Push, null, null, store, store);
		}

		// internal NavigationDataStore を直接使えないため、テスト用の薄い実装
		sealed class InternalNavigationDataStore : INavigationDataReader, INavigationDataWriter
		{
			public bool TryRead<T>(out T data) where T : INavigationData { data = default; return false; }
			public void Write<T>(T data) where T : INavigationData { }
		}

		[UnityTest]
		public System.Collections.IEnumerator Publish_Before_Wait_ResolvesImmediately() => UniTask.ToCoroutine(async () =>
		{
			var ctx = NewCtx();
			ctx.PublishStage<StageA>();
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
			await ctx.WaitForStage<StageA>(cts.Token);
			Assert.Pass();
		});

		[UnityTest]
		public System.Collections.IEnumerator Wait_Before_Publish_ResumesOnPublish() => UniTask.ToCoroutine(async () =>
		{
			var ctx = NewCtx();
			var waiter = ctx.WaitForStage<StageA>();
			Assert.IsFalse(waiter.Status.IsCompleted());
			ctx.PublishStage<StageA>();
			await waiter;
			Assert.Pass();
		});

		[UnityTest]
		public System.Collections.IEnumerator Multiple_Waiters_Broadcast() => UniTask.ToCoroutine(async () =>
		{
			var ctx = NewCtx();
			var w1 = ctx.WaitForStage<StageA>();
			var w2 = ctx.WaitForStage<StageA>();
			ctx.PublishStage<StageA>();
			await UniTask.WhenAll(w1, w2);
			Assert.Pass();
		});

		[UnityTest]
		public System.Collections.IEnumerator Different_Stages_Are_Independent() => UniTask.ToCoroutine(async () =>
		{
			var ctx = NewCtx();
			ctx.PublishStage<StageA>();
			await ctx.WaitForStage<StageA>();

			var waitB = ctx.WaitForStage<StageB>();
			Assert.IsFalse(waitB.Status.IsCompleted(), "B should not resolve from A publish");

			ctx.PublishStage<StageB>();
			await waitB;
		});

		[UnityTest]
		public System.Collections.IEnumerator Wait_Cancellation_Throws_Oce() => UniTask.ToCoroutine(async () =>
		{
			var ctx = NewCtx();
			using var cts = new CancellationTokenSource();
			var waiter = ctx.WaitForStage<StageA>(cts.Token);
			cts.Cancel();
			try
			{
				await waiter;
				Assert.Fail("expected OperationCanceledException");
			}
			catch (OperationCanceledException) { /* expected */ }
		});
	}
}
