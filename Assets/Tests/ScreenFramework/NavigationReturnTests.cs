using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// 新画面を確定する操作（Push / Replace / Change / Reset）が、その新画面のエントリを返すことの検証。
	/// Push との対称性のため Replace / Change / Reset も <see cref="IScreenEntry"/> を返す。
	/// </summary>
	public sealed class NavigationReturnTests
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
			ScreenNavigator.Shutdown().Forget();
			DestroyContainer(_pageContainer);
		}

		[Test]
		public async Task Replace_ReturnsNewEntry()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			var idB = new MarkerScreenId("B");

			var entry = await ScreenNavigator.Page.Replace(idB);

			Assert.IsNotNull(entry);
			Assert.IsTrue(entry.IsAlive);
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Change_ReturnsNewEntry()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			var idC = new MarkerScreenId("C");

			var entry = await ScreenNavigator.Page.Change(idC);

			Assert.IsNotNull(entry);
			Assert.IsTrue(entry.IsAlive);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "Change は単一画面化する");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Reset_ReturnsNewEntry()
		{
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			var idC = new MarkerScreenId("C");

			var entry = await ScreenNavigator.Page.Reset(idC);

			Assert.IsNotNull(entry);
			Assert.IsTrue(entry.IsAlive);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}
	}
}
