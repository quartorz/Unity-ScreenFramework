using System.Collections.Generic;
using System.Threading.Tasks;
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
		public void TearDown() => DestroyContainer(_pageContainer);

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
	}
}
