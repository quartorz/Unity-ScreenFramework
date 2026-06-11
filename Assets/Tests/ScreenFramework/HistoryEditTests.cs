using System.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// History.Edit が _live（履歴と並走する LiveEntry リスト）と同期して編集されることのテスト。
	/// 以前は履歴だけが書き換わり、Edit 後の Pop が別画面を復元したり
	/// IndexOutOfRange になったりするバグがあった。
	/// </summary>
	public sealed class HistoryEditTests
	{
		IScreenContainer _container;
		ScreenNavigatorImpl _nav;

		[SetUp]
		public void SetUp()
		{
			_container = ScreenTestFixtures.NewContainer("HistoryEditRoot");
			// KeepOnCover にして「下の行に生きたインスタンスがある」状態を作れるようにする
			_nav = new ScreenNavigatorImpl(
				new TestServices(),
				ScreenTestFixtures.NewLayer(_container, cache: ScreenCacheMode.KeepOnCover));
		}

		[TearDown]
		public void TearDown()
		{
			ScreenTestFixtures.DestroyContainer(_container);
		}

		[Test]
		public async Task Edit_RemovedLiveRow_IsUnloadedAndPopStaysConsistent()
		{
			var handleA = new InstantHandle();
			var presenterA = new TrackingPresenter();
			await _nav.Push(new ControllableScreenId(handleA, () => presenterA));
			await _nav.Push(new MarkerScreenId("top"));

			// KeepOnCover なので A のインスタンスは生きている。Edit で A の行を履歴から外す
			_nav.History.Edit(e => e.RemoveAt(0));

			Assert.AreEqual(1, _nav.History.Count);
			// 外れた行の生き残りインスタンスは無音で Unload され、補償フックが呼ばれる
			Assert.IsTrue(handleA.UnloadCalled);
			Assert.IsTrue(presenterA.OnAfterUnloadCalled);

			// 履歴 1 枚なので Pop はガードで no-op。内部不整合による例外も出ない
			await _nav.Pop();
			Assert.AreEqual(1, _nav.History.Count);
			Assert.AreEqual(new MarkerScreenId("top"), _nav.Current);
		}

		[Test]
		public async Task Edit_InsertedRow_IsDormant_AndPopLoadsIt()
		{
			await _nav.Push(new MarkerScreenId("home"));
			await _nav.Push(new MarkerScreenId("top"));

			_nav.History.Edit(e => e.Insert(1, new MarkerScreenId("inserted")));

			Assert.AreEqual(3, _nav.History.Count);

			// 挿入行は dormant として入り、Pop で到達した時にロードされて Current になる
			await _nav.Pop();
			Assert.AreEqual(2, _nav.History.Count);
			Assert.AreEqual(new MarkerScreenId("inserted"), _nav.Current);
		}

		[Test]
		public async Task Edit_StackProperty_RawListOps_StaySynced()
		{
			var handleA = new InstantHandle();
			await _nav.Push(new ControllableScreenId(handleA));
			await _nav.Push(new MarkerScreenId("middle"));
			await _nav.Push(new MarkerScreenId("top"));

			// 生の IList 操作（indexer set）でも同期される。差し替えは別画面化なので
			// 元の生き残りインスタンスは破棄される
			_nav.History.Edit(e => e.Stack[0] = new MarkerScreenId("replaced"));

			Assert.AreEqual(3, _nav.History.Count);
			Assert.AreEqual(new MarkerScreenId("replaced"), _nav.History[0]);
			Assert.IsTrue(handleA.UnloadCalled);

			// middle → replaced と順に Pop でき、差し替え後の Identifier がロードされる
			await _nav.Pop();
			Assert.AreEqual(new MarkerScreenId("middle"), _nav.Current);
			await _nav.Pop();
			Assert.AreEqual(new MarkerScreenId("replaced"), _nav.Current);
		}
	}
}
