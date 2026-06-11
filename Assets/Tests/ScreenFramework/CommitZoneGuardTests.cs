using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// 完走必須（commit）ゾーンの Presenter ライフサイクル hook が例外を投げても、遷移本筋が壊れず
	/// _history / _live の bookkeeping が完了することを検証する。
	/// 旧実装は Presenter hook を素通しにしていたため、OnBeforeEnter/OnAfterEnter の throw で
	/// 「見えているのに Navigator が知らない孤児」、OnAfterExit の throw で「隠れたのに Current のまま」になっていた。
	/// 例外はログ（Debug.LogException）に出るので各テストで <see cref="LogAssert.Expect"/> しておく。
	/// </summary>
	public sealed class CommitZoneGuardTests
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
		public async Task Push_OnAfterEnterThrows_StillTracksScreen()
		{
			LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("commit-zone hook threw"));

			var id = new ControllableScreenId(new InstantHandle(), () => new ThrowingHookPresenter(HookKind.AfterEnter));
			// 例外で Push が落ちず、画面は Navigator に追跡される（孤児にならない）。
			await ScreenNavigator.Page.Push(id);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(id, ScreenNavigator.Page.Current);
			Assert.IsNotNull(ScreenNavigator.Page.FindEntry<ThrowingHookPresenter>(),
				"見えているのに Navigator が知らない孤児になってはいけない");
		}

		[Test]
		public async Task Push_OnBeforeEnterThrows_StillTracksScreen()
		{
			LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("commit-zone hook threw"));

			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));

			var idB = new ControllableScreenId(new InstantHandle(), () => new ThrowingHookPresenter(HookKind.BeforeEnter));
			await ScreenNavigator.Page.Push(idB);

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Pop_OnAfterExitThrows_StillUpdatesCurrent()
		{
			LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("commit-zone hook threw"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			var idB = new ControllableScreenId(new InstantHandle(), () => new ThrowingHookPresenter(HookKind.AfterExit));
			await ScreenNavigator.Page.Push(idB);
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);

			// B の退場 hook が落ちても Pop は完走し、Current は A に戻る（隠れたのに Current のままにならない）。
			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Push_OnBeforeLoadThrows_StillPropagates()
		{
			// rollback ゾーン（Load 前）の例外は保護対象外。従来どおり Push の呼び出し側へ伝播する。
			var id = new ControllableScreenId(new InstantHandle(), () => new ThrowingOnBeforeLoadPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "rollback ゾーンの例外は伝播し続ける");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "失敗した Push は追跡されない");
		}

		enum HookKind { BeforeEnter, AfterEnter, AfterExit }

		/// <summary>指定した commit ゾーンの hook で例外を投げる presenter。</summary>
		sealed class ThrowingHookPresenter : IScreenPresenter
		{
			readonly HookKind _kind;
			public ThrowingHookPresenter(HookKind kind) => _kind = kind;

			static UniTask ThrowIf(bool match)
				=> match ? throw new InvalidOperationException("commit-zone hook threw") : UniTask.CompletedTask;

			UniTask IScreenPresenter.OnBeforeEnter(INavigationDataReader r, ITransitionContext ctx, CancellationToken c)
				=> ThrowIf(_kind == HookKind.BeforeEnter);
			UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext ctx, CancellationToken c)
				=> ThrowIf(_kind == HookKind.AfterEnter);
			UniTask IScreenPresenter.OnAfterExit(INavigationDataWriter w, ITransitionContext ctx, CancellationToken c)
				=> ThrowIf(_kind == HookKind.AfterExit);
		}
	}
}
