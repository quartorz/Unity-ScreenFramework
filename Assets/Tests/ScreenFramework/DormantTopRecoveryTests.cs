using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// 戻り先 dormant 画面の復元ロードと、dormant top（_live の末尾が null）からの回復テスト。
	/// <para>
	/// Pop は復元ロードを退場より前（ロールバック可能ゾーン）で行うので、失敗すると Pop がキャンセルされ
	/// 退場画面（top）はそのまま残る。一方 Close は退場後に復元する（完走必須ゾーン）ため、復元が失敗すると
	/// 履歴には Current として残るが _live の対応スロットが null の dormant top になる。
	/// </para>
	/// 後者の状態に対する後続の遷移操作（Pop / PopTo / Replace / Change / Reset / DismissAll）が
	/// ExitPreviousAsync に null を渡して NRE を起こさず、かつ状態を壊さずに完走することを検証する。
	/// </summary>
	public sealed class DormantTopRecoveryTests
	{
		IScreenContainer _pageContainer;

		[TearDown]
		public void TearDown()
		{
			ScreenNavigator.Shutdown().Forget();
			DestroyContainer(_pageContainer);
		}

		void SetupNavigator()
		{
			_pageContainer = NewContainer("PageRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				// DestroyOnCover: 覆われた画面は破棄され、Pop 時に再ロードされる（復元ロードを発生させる前提）
				Page = NewLayer(_pageContainer, cache: ScreenCacheMode.DestroyOnCover),
				Dialog = NewLayer(NewContainer("DialogRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			});
		}

		/// <summary>
		/// Push 時は成功し、復元ロード（2 回目の生成）で失敗する画面 A を作り、Push A → Push B → B を Close で
		/// A を dormant top にする。完了後は履歴 [A]（Count==1）・_live 末尾 null・Current==A の状態。
		/// <para>
		/// Close を使うのは、復元ロードが退場より後（完走必須ゾーン）で走る経路だから。Pop は復元を退場より前に
		/// 行うので、失敗しても top が残るだけで dormant top にはならない。
		/// </para>
		/// </summary>
		async UniTask<ControllableScreenId> MakeDormantSingleTop()
		{
			var creations = 0;
			var idA = new ControllableScreenId(new InstantHandle(), () =>
				++creations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			await ScreenNavigator.Page.Push(idA);
			var entryB = await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			try { await entryB.Close(); }
			catch (InvalidOperationException) { /* 復元ロード失敗は想定どおり */ }
			return idA;
		}

		/// <summary>
		/// 履歴 [A,B] で top B を dormant にする。Push A,B,C → C を Close して B の復元ロードを失敗させる。
		/// </summary>
		async UniTask<(ControllableScreenId idA, ControllableScreenId idB)> MakeDormantTopAboveOne()
		{
			var idA = new ControllableScreenId(new InstantHandle(), () => new NullPresenter());
			var bCreations = 0;
			var idB = new ControllableScreenId(new InstantHandle(), () =>
				++bCreations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);
			var entryC = await ScreenNavigator.Page.Push(new MarkerScreenId("C"));
			try { await entryC.Close(); }
			catch (InvalidOperationException) { /* B の復元失敗は想定どおり */ }
			return (idA, idB);
		}

		[Test]
		public async Task Replace_OntoDormantTop_Succeeds_WithoutNre()
		{
			SetupNavigator();
			var idA = await MakeDormantSingleTop();
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "前提: A は dormant top として履歴に残っている");

			var idC = new MarkerScreenId("C");
			var entry = await ScreenNavigator.Page.Replace(idC);

			Assert.IsNotNull(entry, "dormant top への Replace は新画面のエントリを返す");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "dormant top が新画面へ差し替わる");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idD = new MarkerScreenId("D");
			await ScreenNavigator.Page.Push(idD);
			Assert.AreSame(idD, ScreenNavigator.Page.Current, "差し替え後も次の Push が成立する");
		}

		[Test]
		public async Task Change_OntoDormantTop_Succeeds_WithoutNre()
		{
			SetupNavigator();
			var idA = await MakeDormantSingleTop();
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "前提: A は dormant top");

			var idC = new MarkerScreenId("C");
			var entry = await ScreenNavigator.Page.Change(idC);

			Assert.IsNotNull(entry, "dormant top への Change は新画面のエントリを返す");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "dormant top が新画面へ差し替わる");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idD = new MarkerScreenId("D");
			await ScreenNavigator.Page.Push(idD);
			Assert.AreSame(idD, ScreenNavigator.Page.Current, "差し替え後も次の Push が成立する");
		}

		// ===========================================================================
		// Pop の復元ロードはロールバック可能ゾーン（失敗しても top は退場しない）
		// ===========================================================================

		[Test]
		public async Task Pop_DormantBelow_LoadFails_CancelsPop_AndPreservesTop()
		{
			// Push A,B,C → Pop で B の復元ロードが失敗した場合、C（top）は退場せずそのまま残る。
			// 復元を退場より後で行っていた頃は、失敗すると C が消えた状態になっていた。
			SetupNavigator();
			var idA = new ControllableScreenId(new InstantHandle(), () => new NullPresenter());
			var bCreations = 0;
			var idB = new ControllableScreenId(new InstantHandle(), () =>
				++bCreations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);
			await ScreenNavigator.Page.Push(idC);

			try { await ScreenNavigator.Page.Pop(); }
			catch (InvalidOperationException) { /* B の復元失敗は想定どおり */ }

			Assert.AreEqual(3, ScreenNavigator.Page.History.Count, "Pop キャンセルで履歴は変わらない");
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "top(C) は退場せずそのまま残る");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task Pop_DormantBelow_LoadSucceeds_Completes()
		{
			// 戻り先が dormant でも復元ロードが成功すれば通常どおり Pop 完了する。
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task Pop_DormantBelow_LoadFails_ThenRetry_Succeeds()
		{
			// 1 回目の Pop で復元ロードが失敗してキャンセル、2 回目の Pop で成功する。
			SetupNavigator();
			var idA = new ControllableScreenId(new InstantHandle(), () => new NullPresenter());
			var bCreations = 0;
			var idB = new ControllableScreenId(new InstantHandle(), () =>
				// 1 回目=Push 時成功、2 回目=復元失敗、3 回目=復元成功
				++bCreations == 2 ? (IScreenPresenter)new ThrowingOnBeforeLoadPresenter() : new NullPresenter());
			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);  // bCreations=1
			await ScreenNavigator.Page.Push(idC);

			// 1 回目: B の復元（bCreations=2）が失敗 → Pop キャンセル、C が残る
			try { await ScreenNavigator.Page.Pop(); }
			catch (InvalidOperationException) { /* B の復元失敗は想定どおり */ }

			Assert.AreEqual(3, ScreenNavigator.Page.History.Count, "Pop キャンセルで履歴は変わらない");
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "C は退場せず残っている");

			// 2 回目: B の復元（bCreations=3）は成功 → Pop 完了
			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task PopTo_WithDormantIntermediate_SkipsAndReachesTarget()
		{
			// PopTo は中間エントリを黙って破棄する。中間に dormant 行があっても目的の画面に到達する。
			SetupNavigator();
			var (idA, _) = await MakeDormantTopAboveOne();
			await ScreenNavigator.Page.Push(new MarkerScreenId("D"));   // dormant B の上に 1 枚積む

			Assert.AreEqual(3, ScreenNavigator.Page.History.Count, "前提: 履歴 [A,B(dormant),D]");

			await ScreenNavigator.Page.PopTo(id => ReferenceEquals(id, idA));

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "dormant 中間エントリを跨いで A に到達する");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		// ===========================================================================
		// dormant top（Close の復元失敗で生じる）からの回復
		// ===========================================================================

		[Test]
		public async Task Pop_OffDormantTop_RestoresBelow_WithoutNre()
		{
			// dormant top の下にもう 1 枚あるケース（Count>=2）。続く Pop が top=null を退場に渡さず、
			// A を復元して完走する。
			SetupNavigator();
			var (idA, idB) = await MakeDormantTopAboveOne();

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "前提: 履歴 [A,B] で top B が dormant");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);

			await ScreenNavigator.Page.Pop();   // 修正前は top=null を退場に渡して NRE

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "dormant top の Pop で下の A が復元される");
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task PopTo_AcrossDormantTop_ReachesTarget_WithoutNre()
		{
			// PopTo は最終段で PopCore を通る。dormant top を経由しても目的の画面に到達する。
			SetupNavigator();
			var (idA, _) = await MakeDormantTopAboveOne();

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "前提: 履歴 [A,B] で top B が dormant");

			await ScreenNavigator.Page.PopTo(id => ReferenceEquals(id, idA));   // 修正前は NRE

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "dormant top を跨いで A に到達する");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task Reset_OntoDormantTop_CollapsesToNewScreen_WithoutNre()
		{
			// Reset の全破棄(DismissAllInternal)は dormant top(null 行)を退場なしで畳み、新画面 1 枚にする。
			SetupNavigator();
			var idA = await MakeDormantSingleTop();
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "前提: A は dormant top");

			var idC = new MarkerScreenId("C");
			var entry = await ScreenNavigator.Page.Reset(idC);

			Assert.IsNotNull(entry, "dormant top があっても Reset は新画面のエントリを返す");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task DismissAll_WithDormantTop_ClearsEverything_WithoutNre()
		{
			SetupNavigator();
			var idA = await MakeDormantSingleTop();
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "前提: A は dormant top");

			await ScreenNavigator.Page.DismissAll();

			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "dormant top を含む全行が畳まれる");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "空になった後も次の Push が成立する");
		}
	}
}
