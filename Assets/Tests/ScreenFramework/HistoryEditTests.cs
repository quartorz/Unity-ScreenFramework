using System;
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
	/// History.Edit のうち <b>MBT 語彙外</b>のもの（callback の中から遷移 API を呼ぶ／編集をネストする
	/// 異常系：スナップショット陳腐化・遅延消化・例外隔離）だけを検証する。
	/// 基本の _live 同期はモデルベーステスト（ModelBased/）がカバーする。
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

		[Test]
		public async Task Edit_NestedEditInCallback_IsDeferredAndBothApply()
		{
			// callback 内からネストして Edit を呼ぶと、外側の編集の適用完了後に適用される。
			// 即時適用すると外側のスナップショットが古くなり、ネスト側で破棄済みの LiveEntry が
			// _live に復活する（unload 済みの zombie 行）か、外側の編集が誤って破棄されるかのどちらかになる。
			var handleA = new InstantHandle();
			var handleB = new InstantHandle();
			await _nav.Push(new ControllableScreenId(handleA));
			await _nav.Push(new ControllableScreenId(handleB));
			await _nav.Push(new MarkerScreenId("top"));

			_nav.History.Edit(e =>
			{
				e.RemoveAt(1);   // B を外す
				_nav.History.Edit(e2 => e2.RemoveAt(0));   // ネスト: A を外す（外側の適用後に処理される）
			});

			Assert.AreEqual(1, _nav.History.Count, "外側 → ネストの順で両方の編集が適用される");
			Assert.AreEqual(new MarkerScreenId("top"), _nav.Current);
			Assert.IsTrue(handleB.UnloadCalled, "外側の編集で外れた B は Unload される");
			Assert.IsTrue(handleA.UnloadCalled, "ネストした編集で外れた A も Unload される");
		}

		[Test]
		public async Task Edit_NestedEditThrows_OuterStillApplies_AndNestedFaultIsLogged()
		{
			// ネストした編集は遅延消化されるため、その例外は外側の Edit 呼び出しへは伝播せず
			// ログに留まる（遷移中に遅延された Edit の例外と同じ扱い）。外側の編集は壊れない。
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at nested Edit"));
			var handleA = new InstantHandle();
			await _nav.Push(new ControllableScreenId(handleA));
			await _nav.Push(new MarkerScreenId("top"));

			_nav.History.Edit(e =>
			{
				e.RemoveAt(0);
				_nav.History.Edit(_ => throw new InvalidOperationException("fault injected at nested Edit"));
			});

			Assert.AreEqual(1, _nav.History.Count, "ネストした編集の失敗で外側の編集は壊れない");
			Assert.IsTrue(handleA.UnloadCalled);

			// フォールト後も以後の編集は通常どおり成立する
			_nav.History.Edit(e => e.Insert(0, new MarkerScreenId("inserted")));
			Assert.AreEqual(2, _nav.History.Count);
		}
	}
}
