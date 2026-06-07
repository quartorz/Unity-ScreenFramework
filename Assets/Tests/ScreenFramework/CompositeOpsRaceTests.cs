using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// 複合操作 (Change / PopTo / DismissAll) が in-flight な遷移と並走したときに
	/// `_history` / `_live` の整合性を保つことを検証する。
	/// 修正方針: 複合操作全体を 1 つの Run に閉じる(<see cref="ScreenNavigator"/> 実装側)。
	/// </summary>
	public sealed class CompositeOpsRaceTests
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
		public async Task DismissAll_DuringInflightPush_AlsoDismissesIt()
		{
			// pushA が Load 中(history 未追加)で DismissAll を呼ぶ → 旧実装は history.Count==0 を見て即抜け
			// → pushA が後追いで history に積まれる → history.Count==1 で残る。
			// 修正後: DismissAll が Run を通って pushA を Preempt → history.Count==0。
			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var pushA = ScreenNavigator.Page.Push(
				new ControllableScreenId(new ControllableHandle(source)));
			await UniTask.Yield();

			var dismissTask = ScreenNavigator.Page.DismissAll();

			source.TrySetResult(new NopView());
			await pushA.SuppressCancellationThrow();
			await dismissTask;

			Assert.AreEqual(0, ScreenNavigator.Page.History.Count,
				"DismissAll should preempt / wait for in-flight Push and end with empty history");
		}

		[Test]
		public async Task Change_DuringInflightPushInCommitZone_ResultsInSingleHistoryEntry()
		{
			// in-flight pushC を「完走必須ゾーン」(OnBeforeEnter, safeCt=None) で止める。
			// ここに到達済みの push は Preempt 不能で signal 待ちになり、Change が事前計算した履歴と食い違う：
			//   ClearAllExceptCurrent: history=[A,B] → [B]
			//   Replace Run は ctsC.Cancel しても効かず signalC 待ち
			//   gate 解放 → pushC が history.Push(C) → [B,C]
			//   ReplaceCore が top=entryC を D に差し替え → history=[B,D]
			// 期待: [D] のみ。バグ時: [B,D]
			var idA = new MarkerScreenId("A");
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);

			var gate = new GatedPresenter();
			var pushC = ScreenNavigator.Page.Push(
				new ControllableScreenId(new InstantHandle(), () => gate));
			await gate.Started;

			var idD = new MarkerScreenId("D");
			var changeTask = ScreenNavigator.Page.Change(idD);

			await UniTask.Yield();
			gate.Release();
			await pushC.SuppressCancellationThrow();
			await changeTask;

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count,
				"Change で除去したはずの B が残っていたら Run 外で history を mutate しているバグ");
			Assert.AreSame(idD, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task PopTo_DuringInflightPushInCommitZone_PopsAllAboveTarget()
		{
			// 同じ仕掛けで PopTo を落とす。
			// PopTo(A) の Pop Run は ctsC.Cancel しても効かず signalC 待ち。
			// gate 解放後 history=[A,B,C] になってから Pop が C 1 枚だけ剥がす → [A,B]。
			// 期待: [A] のみ。バグ時: [A,B]。
			var idA = new MarkerScreenId("A");
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);

			var gate = new GatedPresenter();
			var pushC = ScreenNavigator.Page.Push(
				new ControllableScreenId(new InstantHandle(), () => gate));
			await gate.Started;

			var popToTask = ScreenNavigator.Page.PopTo(id => ReferenceEquals(id, idA));
			await UniTask.Yield();

			gate.Release();
			await pushC.SuppressCancellationThrow();
			await popToTask;

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count,
				"PopTo(A) なのに B が残っていたら Pop と in-flight push が並走しているバグ");
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
		}
	}
}
