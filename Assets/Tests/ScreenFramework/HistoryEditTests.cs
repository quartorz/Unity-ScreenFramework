using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;

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

		[Test]
		public async Task Edit_DuringTransition_IsDeferredUntilCompletion()
		{
			var idA = new MarkerScreenId("A");
			var idB = new MarkerScreenId("B");
			await _nav.Push(idA);
			await _nav.Push(idB);

			// OnBeforeEnter でブロックする画面を Push して「遷移中」を作る。
			// bookkeeping は Enter hook の前に済むので、この時点で G は既に履歴に積まれている。
			var gate = new GatedPresenter();
			var idG = new ControllableScreenId(new InstantHandle(), () => gate);
			var pushG = _nav.Push(idG); // await しない
			await gate.Started;

			Assert.IsTrue(_nav.IsTransitioning);
			Assert.AreEqual(3, _nav.History.Count);

			// 遷移中の Edit は index 競合を避けるため遅延される。
			_nav.History.Edit(e => e.RemoveAt(0));
			Assert.AreEqual(3, _nav.History.Count, "遷移中の Edit は即時適用されない");

			gate.Release();
			await pushG;

			// チェーン完了後にまとめて適用される。
			Assert.AreEqual(2, _nav.History.Count, "遷移完了後に Edit が適用される");
			Assert.AreEqual(idB, _nav.History[0]);
		}

		[Test]
		public async Task DeferredEdit_StartingAsyncTransition_DefersRemainingEdits()
		{
			await _nav.Push(new MarkerScreenId("A"));
			await _nav.Push(new MarkerScreenId("B"));

			// 遷移中を作って Edit を 2 件遅延させる
			var gate = new GatedPresenter();
			var pushBlocker = _nav.Push(new ControllableScreenId(new InstantHandle(), () => gate));
			await gate.Started;

			// 1 件目の Edit は callback から非同期ロードの Push を発行する（履歴自体は編集しない）。
			// 2 件目の Edit はその Push の遷移が完了するまで適用されてはならない。
			var loadSource = new UniTaskCompletionSource<IScreenViewInstance>();
			UniTask<IScreenEntry> pushFromEdit = default;
			_nav.History.Edit(_ => pushFromEdit = _nav.Push(new ControllableScreenId(new ControllableHandle(loadSource))));
			_nav.History.Edit(e => e.RemoveAt(0)); // A を消す

			gate.Release();
			await pushBlocker;

			Assert.IsTrue(_nav.IsTransitioning, "1 件目の Edit が発行した Push はロード待ちで遷移中");
			Assert.AreEqual(3, _nav.History.Count, "新しい遷移中に残りの Edit を適用しない");

			loadSource.TrySetResult(new NopView());
			await pushFromEdit;

			Assert.AreEqual(3, _nav.History.Count, "Push 完了で 4 行 → 残りの Edit（A 削除）で 3 行");
			Assert.AreEqual(new MarkerScreenId("B"), _nav.History[0], "チェーン完了後に残りの Edit が適用され A が消える");
		}

		[Test]
		public async Task Edit_CallbackStartingTransitionThatMutatesStack_IsDiscardedWithError()
		{
			await _nav.Push(new MarkerScreenId("A"));
			await _nav.Push(new MarkerScreenId("B"));

			LogAssert.Expect(LogType.Error, new Regex(@"History\.Edit"));

			// callback 内の Push（同期完了）でスタックが動くと、編集前に取ったスナップショットが古くなり、
			// そのまま適用すると Push が積んだ行が履歴から消えてしまう。この場合は編集の方を破棄する。
			var idC = new MarkerScreenId("C");
			_nav.History.Edit(e =>
			{
				e.RemoveAt(0);
				_nav.Push(idC).Forget();
			});

			Assert.AreEqual(3, _nav.History.Count, "Push は成立し、編集は破棄される");
			Assert.AreEqual(new MarkerScreenId("A"), _nav.History[0], "RemoveAt(0) は適用されない");
			Assert.AreEqual(idC, _nav.Current);
		}
	}
}
