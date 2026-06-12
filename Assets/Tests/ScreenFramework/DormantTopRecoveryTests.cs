using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// dormant top（_live の末尾が null）からの回復テスト。
	/// DestroyOnCover の画面を Pop で復元するときにロードが失敗すると、履歴には Current として残るが
	/// 並走する _live の対応スロットは null（dormant）のまま残る。この状態に対する後続の遷移操作
	/// （Pop / PopTo / Replace / Change）が ExitPreviousAsync に null を渡して NRE を起こさず、
	/// かつ状態を壊さずに完走することを検証する。
	/// 既存の <see cref="FaultInjectionTests"/> は「dormant 化した直後に Push で復帰できる」ことだけを
	/// 見ており、より自然な「もう一度戻る / 差し替える」経路が未検証だった。
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
		/// Push 時は成功し、復元ロード（2 回目の生成）で失敗する画面 A を作り、Push A → Push B → Pop で
		/// A を dormant top にする。完了後は履歴 [A]（Count==1）・_live 末尾 null・Current==A の状態。
		/// </summary>
		async UniTask<ControllableScreenId> MakeDormantSingleTop()
		{
			var creations = 0;
			var idA = new ControllableScreenId(new InstantHandle(), () =>
				++creations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			try { await ScreenNavigator.Page.Pop(); }
			catch (InvalidOperationException) { /* 復元ロード失敗は想定どおり */ }
			return idA;
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

		[Test]
		public async Task Pop_OffDormantTop_RestoresBelow_WithoutNre()
		{
			// dormant top の下にもう 1 枚あるケース（Count>=2）。Push A,B,C → Pop で B の復元が失敗し、
			// 履歴 [A,B]・top B が dormant になる。続く Pop が top=null を退場に渡さず、A を復元して完走する。
			SetupNavigator();
			var idA = new ControllableScreenId(new InstantHandle(), () => new NullPresenter());
			var bCreations = 0;
			var idB = new ControllableScreenId(new InstantHandle(), () =>
				++bCreations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);
			await ScreenNavigator.Page.Push(new MarkerScreenId("C"));

			try { await ScreenNavigator.Page.Pop(); }
			catch (InvalidOperationException) { /* B の復元失敗は想定どおり */ }

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
			var idA = new ControllableScreenId(new InstantHandle(), () => new NullPresenter());
			var bCreations = 0;
			var idB = new ControllableScreenId(new InstantHandle(), () =>
				++bCreations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);
			await ScreenNavigator.Page.Push(new MarkerScreenId("C"));

			try { await ScreenNavigator.Page.Pop(); }
			catch (InvalidOperationException) { /* B の復元失敗は想定どおり */ }

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "前提: 履歴 [A,B] で top B が dormant");

			await ScreenNavigator.Page.PopTo(id => ReferenceEquals(id, idA));   // 修正前は NRE

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "dormant top を跨いで A に到達する");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}
	}
}
