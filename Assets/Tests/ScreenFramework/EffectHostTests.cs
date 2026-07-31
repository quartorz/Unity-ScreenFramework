using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;

namespace Tests.ScreenFramework
{
	using static FaultInjectionFixtures;

	/// <summary>
	/// EffectHost の Sorting Order 採番。狙いは「Page と Dialog が互いを知らないまま同一 host を共有しても、
	/// 同時に走る Effect の order が衝突しない」こと ── レイヤー間に共有が無い設計上、調停できるのは host だけ
	/// なので、その採番ロジックを直接検証する。あわせて EffectRunner が遷移完了で order を返却する配線も見る。
	/// </summary>
	public sealed class EffectHostTests
	{
		EffectHost _host;

		[SetUp]
		public void SetUp() => _host = new GameObject("EffectHost").AddComponent<EffectHost>();

		[TearDown]
		public void TearDown()
		{
			if (_host != null) Object.DestroyImmediate(_host.gameObject);
		}

		[Test]
		public void FirstLease_ReturnsBaseOrder()
		{
			Assert.AreEqual(0, _host.LeaseOrder(), "既定の baseOrder は 0");
		}

		[Test]
		public void SharedHost_ConcurrentLeasesFromTwoLayers_GetDistinctOrders()
		{
			// Page と Dialog が同じ host を共有し、両者の Effect が同時に走っている状況。
			var pageOrder = _host.LeaseOrder();
			var dialogOrder = _host.LeaseOrder();
			Assert.AreNotEqual(pageOrder, dialogOrder,
				"共有 host では同時に走る Effect 同士の order が被ってはならない");
		}

		[Test]
		public void ConcurrentLeases_AreSteppedByOrderStep()
		{
			var orders = new[] { _host.LeaseOrder(), _host.LeaseOrder(), _host.LeaseOrder() };
			CollectionAssert.AllItemsAreUnique(orders);
			CollectionAssert.AreEqual(new[] { 0, 10, 20 }, orders, "既定の orderStep=10 で段積みされる");
		}

		[Test]
		public void ReleaseOrder_DoesNotReuse_NextAllocationAlwaysHigherThanCurrent()
		{
			var first = _host.LeaseOrder();   // 0
			var second = _host.LeaseOrder();  // 10（走行継続中）
			_host.ReleaseOrder(first);        // 0 を返却

			var third = _host.LeaseOrder();
			Assert.AreEqual(20, third,
				"返却された order は再利用されず、次の lease は常に現在の最大値より大きい値が割り当てられる");
		}

		[Test]
		public void Release_OfNonOutstandingOrder_IsHarmless()
		{
			Assert.DoesNotThrow(() => _host.ReleaseOrder(9999));
			Assert.AreEqual(0, _host.LeaseOrder(), "未貸出 order の返却は何もしない");
		}

		// ---------------------------------------------------------------------------
		// EffectRunner の返却配線: 遷移完了(Finish→DestroyNow)で借りた order が host に戻ること。
		// Addressables を介した実 Instantiate は EditMode 不可なので、lease 済みの内部状態を
		// Reflection で再現してから Finish を呼ぶ（NewLoadedEffectRunner と同じ流儀）。
		// ---------------------------------------------------------------------------

		[Test]
		public void EffectRunner_Finish_ReturnsLeasedOrderToHost()
		{
			var host = new RecordingEffectHost();
			var runner = new EffectRunner(prefabRef: null, host, stagingParent: null, NewBareTransitionContext());

			var go = new GameObject("FakeEffectInstance");
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
			// LoadAndInstantiateAsync が instantiate 成功時にやることを再現: 1 枠 lease して掴む。
			var leased = host.LeaseOrder();
			typeof(EffectRunner).GetField("_instanceGo", flags).SetValue(runner, go);
			typeof(EffectRunner).GetField("_leasedOrder", flags).SetValue(runner, leased);
			typeof(EffectRunner).GetField("_hasLease", flags).SetValue(runner, true);

			runner.Finish();

			Assert.IsTrue(go == null, "Finish で Effect インスタンスは破棄される");
			CollectionAssert.AreEqual(new[] { leased }, host.Released, "借りた order がちょうど 1 回返却される");
			Assert.AreEqual(0, host.Outstanding, "返却後に貸出残が無い（slot leak しない）");
		}

		[Test]
		public void EffectRunner_FinishCalledTwice_ReleasesOnlyOnce()
		{
			var host = new RecordingEffectHost();
			var runner = new EffectRunner(prefabRef: null, host, stagingParent: null, NewBareTransitionContext());

			var go = new GameObject("FakeEffectInstance");
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
			var leased = host.LeaseOrder();
			typeof(EffectRunner).GetField("_instanceGo", flags).SetValue(runner, go);
			typeof(EffectRunner).GetField("_leasedOrder", flags).SetValue(runner, leased);
			typeof(EffectRunner).GetField("_hasLease", flags).SetValue(runner, true);

			runner.Finish();
			runner.Finish();

			Assert.AreEqual(1, host.Released.Count, "二重 Finish でも order の返却は 1 回だけ");
		}

		/// <summary>lease / release を記録するだけの IEffectHost。採番は単純インクリメント。</summary>
		sealed class RecordingEffectHost : IEffectHost
		{
			readonly HashSet<int> _outstanding = new HashSet<int>();
			int _next;

			public List<int> Released { get; } = new List<int>();
			public int Outstanding => _outstanding.Count;

			public Transform Root => null;
			public Camera RenderCamera => null;
			public int SortingLayerId => 0;

			public int LeaseOrder()
			{
				var order = _next;
				_next += 10;
				_outstanding.Add(order);
				return order;
			}

			public void ReleaseOrder(int order)
			{
				Released.Add(order);
				_outstanding.Remove(order);
			}
		}
	}
}
