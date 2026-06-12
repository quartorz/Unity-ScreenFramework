using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// <see cref="IScreenNavigator.OnTransitionStart"/> / <see cref="IScreenNavigator.OnTransitionEnd"/> が
	/// 各 public API の caller intent に対応する <see cref="ScreenTransitionKind"/> で 1 発 fire されることを検証する。
	/// 内部の Core 呼出が二重に fire しないこと、Change/Reset/PopTo がそれぞれ自身の Kind で通知されることが要点。
	/// </summary>
	public sealed class TransitionEventTests
	{
		IScreenContainer _pageContainer;

		[SetUp]
		public void SetUp()
		{
			_pageContainer = NewContainer("PageRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer),
				Dialog = NewLayer(NewContainer("DialogRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			});
		}

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード（既初期化なら throw）があるので、各テスト後に静的参照を畳む。
			ScreenNavigator.Shutdown().Forget();
			DestroyContainer(_pageContainer);
		}

		[Test]
		public async Task Change_FiresTransitionWithKindChange()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));

			var startKinds = new List<ScreenTransitionKind>();
			var endKinds = new List<ScreenTransitionKind>();
			ScreenNavigator.Page.OnTransitionStart += e => startKinds.Add(e.Kind);
			ScreenNavigator.Page.OnTransitionEnd += e => endKinds.Add(e.Kind);

			await ScreenNavigator.Page.Change(new MarkerScreenId("B"));

			Assert.Contains(ScreenTransitionKind.Change, startKinds);
			Assert.Contains(ScreenTransitionKind.Change, endKinds);
			CollectionAssert.DoesNotContain(startKinds, ScreenTransitionKind.Replace,
				"内部の ReplaceCore は fire しないこと");
		}

		[Test]
		public async Task Reset_FiresTransitionWithKindReset()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));

			var startKinds = new List<ScreenTransitionKind>();
			var endKinds = new List<ScreenTransitionKind>();
			ScreenNavigator.Page.OnTransitionStart += e => startKinds.Add(e.Kind);
			ScreenNavigator.Page.OnTransitionEnd += e => endKinds.Add(e.Kind);

			await ScreenNavigator.Page.Reset(new MarkerScreenId("B"));

			Assert.Contains(ScreenTransitionKind.Reset, startKinds);
			Assert.Contains(ScreenTransitionKind.Reset, endKinds);
			CollectionAssert.DoesNotContain(startKinds, ScreenTransitionKind.Push,
				"内部の PushCore は fire しないこと");
		}

		[Test]
		public async Task PopTo_FiresTransitionWithKindPopTo()
		{
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("C"));

			var startKinds = new List<ScreenTransitionKind>();
			var endKinds = new List<ScreenTransitionKind>();
			ScreenNavigator.Page.OnTransitionStart += e => startKinds.Add(e.Kind);
			ScreenNavigator.Page.OnTransitionEnd += e => endKinds.Add(e.Kind);

			await ScreenNavigator.Page.PopTo(id => ReferenceEquals(id, idA));

			Assert.Contains(ScreenTransitionKind.PopTo, startKinds);
			Assert.Contains(ScreenTransitionKind.PopTo, endKinds);
			CollectionAssert.DoesNotContain(startKinds, ScreenTransitionKind.Pop,
				"内部の PopCore は fire しないこと");
		}

		[Test]
		public async Task CloseTop_FiresCloseKind_NotPop()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			var b = await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			var endKinds = new List<ScreenTransitionKind>();
			ScreenNavigator.Page.OnTransitionEnd += e => endKinds.Add(e.Kind);

			await b.Close();

			CollectionAssert.Contains(endKinds, ScreenTransitionKind.Close);
			CollectionAssert.DoesNotContain(endKinds, ScreenTransitionKind.Pop, "Close は Pop を騙らない");
		}

		[Test]
		public async Task CloseMiddle_FiresCloseKind()
		{
			// 中間 Close は中間画面が生きている必要がある。既定レイヤーは Cover+DestroyOnCover で
			// 覆われた時点で中間は破棄され Close 対象が消えるため、KeepOnCover で初期化し直す。
			await ScreenNavigator.Shutdown();
			DestroyContainer(_pageContainer);
			_pageContainer = NewContainer("PageRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer, cache: ScreenCacheMode.KeepOnCover),
				Dialog = NewLayer(NewContainer("DialogRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			});

			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			var b = await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("C"));

			var startKinds = new List<ScreenTransitionKind>();
			ScreenNavigator.Page.OnTransitionStart += e => startKinds.Add(e.Kind);

			await b.Close(); // 中間を閉じる（旧実装は無発火だった）

			CollectionAssert.Contains(startKinds, ScreenTransitionKind.Close);
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task DismissAll_FiresDismissAllKind()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			var startKinds = new List<ScreenTransitionKind>();
			ScreenNavigator.Page.OnTransitionStart += e => startKinds.Add(e.Kind);

			await ScreenNavigator.Page.DismissAll();

			CollectionAssert.Contains(startKinds, ScreenTransitionKind.DismissAll);
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
		}

		// 失敗 / キャンセルされた遷移の End イベント（Succeeded=false）は
		// FaultInjectionInfraTests.FailedPush_StillFiresTransitionEnd_WithSucceededFalse /
		// CanceledPush_FiresTransitionEnd_WithSucceededFalse が発火回数・Kind 込みで検証している。

		[Test]
		public async Task SuccessfulPush_EndEvent_HasSucceededTrue()
		{
			ScreenTransitionEvent? end = null;
			ScreenNavigator.Page.OnTransitionEnd += e => end = e;

			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));

			Assert.IsTrue(end.HasValue);
			Assert.IsTrue(end.Value.Succeeded);
		}
	}
}
