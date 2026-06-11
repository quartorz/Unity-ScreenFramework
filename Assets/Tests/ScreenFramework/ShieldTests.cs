using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sample;
using ScreenFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// レイヤー Canvas の動的生成（config.Camera 連動、ScreenFramework 側）と、
	/// Sample 側の InputShield / ShieldRegistry / WithLoadingShield の挙動を検証する。
	/// Tests は EditMode アセンブリのため MonoBehaviour の Awake/OnEnable は自動実行されない。
	/// よって InputShield のレジストリ登録は手動で行い、可視判定は IsVisible（参照カウント）で見る
	/// （Canvas/GraphicRaycaster の enabled トグルは Awake 後にしか効かないが、参照カウントは Awake 非依存）。
	/// </summary>
	public sealed class ShieldTests
	{
		// ---- レイヤー Canvas 動的生成 ----

		[Test]
		public void LayerCanvas_CreatedUnderContainer_WhenCameraProvided()
		{
			var container = NewContainer("PageRoot");
			var cam = new GameObject("cam", typeof(Camera)).GetComponent<Camera>();
			try
			{
				_ = new ScreenNavigatorImpl(new TestServices(), new ScreenLayerConfig
				{
					Container = container,
					Camera = cam,
					SortingOrder = 50,
				});

				var canvasTf = container.Root.Find("ScreenFramework.LayerCanvas");
				Assert.IsNotNull(canvasTf, "Camera 指定時はレイヤー Canvas が動的生成される");
				var canvas = canvasTf.GetComponent<Canvas>();
				Assert.IsNotNull(canvas);
				Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode);
				Assert.AreSame(cam, canvas.worldCamera);
				Assert.AreEqual(50, canvas.sortingOrder);
			}
			finally
			{
				DestroyContainer(container);
				Object.DestroyImmediate(cam.gameObject);
			}
		}

		[Test]
		public void LayerCanvas_NotCreated_WhenNoCamera()
		{
			var container = NewContainer("PageRoot");
			try
			{
				_ = new ScreenNavigatorImpl(new TestServices(), new ScreenLayerConfig
				{
					Container = container,
					// Camera 未指定 = 従来動作（Canvas はシーン任せ）
				});

				Assert.IsNull(container.Root.Find("ScreenFramework.LayerCanvas"),
					"Camera 未指定なら Canvas を作らない（既存構成・テストに無影響）");
			}
			finally
			{
				DestroyContainer(container);
			}
		}

		[Test]
		public async Task ScreenCanvas_SortingOrder_AssignedByStackIndex()
		{
			var cam = new GameObject("cam", typeof(Camera)).GetComponent<Camera>();
			var container = NewContainer("PageRoot");
			var go1 = new GameObject("view1", typeof(Canvas));
			var go2 = new GameObject("view2", typeof(Canvas));
			try
			{
				var nav = new ScreenNavigatorImpl(new TestServices(), new ScreenLayerConfig
				{
					Container = container,
					Camera = cam,
					SortingOrder = 10,
					StackMode = StackMode.Stack, // 2 枚同時表示にして重なり順を見る
					DefaultModal = false,        // blocker は別関心なのでオフ
				});

				await nav.Push(new CanvasViewScreenId(go1));
				await nav.Push(new CanvasViewScreenId(go2));

				var c1 = go1.GetComponent<Canvas>();
				var c2 = go2.GetComponent<Canvas>();
				Assert.IsTrue(c1.overrideSorting);
				Assert.IsTrue(c2.overrideSorting);
				Assert.AreEqual(10, c1.sortingOrder, "stack index 0 = base");
				Assert.AreEqual(12, c2.sortingOrder, "stack index 1 = base + step(2)");
			}
			finally
			{
				// view は LayerCanvas(container 配下)の子になっているので、先に個別破棄してから container を破棄する。
				Object.DestroyImmediate(go1);
				Object.DestroyImmediate(go2);
				DestroyContainer(container);
				Object.DestroyImmediate(cam.gameObject);
			}
		}

		// ---- ShieldRegistry / InputShield ----

		[Test]
		public void ShieldRegistry_RegisterGetUnregister()
		{
			var go = new GameObject("shield");
			var shield = go.AddComponent<InputShield>();
			try
			{
				ShieldRegistry.Register(shield);
				Assert.AreSame(shield, ShieldRegistry.Get(shield.Key));

				ShieldRegistry.Unregister(shield);
				Assert.IsNull(ShieldRegistry.Get(shield.Key));
			}
			finally
			{
				ShieldRegistry.Unregister(shield);
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void Shield_ShowHide_IsRefCounted()
		{
			var go = new GameObject("shield");
			var shield = go.AddComponent<InputShield>();
			try
			{
				Assert.IsFalse(shield.IsVisible);
				shield.Show();
				shield.Show();
				Assert.IsTrue(shield.IsVisible);
				shield.Hide();
				Assert.IsTrue(shield.IsVisible, "参照カウントが残っている間は表示継続");
				shield.Hide();
				Assert.IsFalse(shield.IsVisible);
				shield.Hide();
				Assert.IsFalse(shield.IsVisible, "0 のとき Hide しても破綻しない");
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		// ---- WithLoadingShield ----

		[Test]
		public async Task WithLoadingShield_ShowsDuringTask_HidesAfter()
		{
			var (go, shield) = NewRegisteredShield();
			try
			{
				Assert.IsFalse(shield.IsVisible);

				var gate = new UniTaskCompletionSource();
				var task = gate.Task.WithLoadingShield(shield.Key);
				Assert.IsTrue(shield.IsVisible, "タスク実行中は表示");

				gate.TrySetResult();
				await task;
				Assert.IsFalse(shield.IsVisible, "完了後は非表示");
			}
			finally { Cleanup(go, shield); }
		}

		[Test]
		public async Task WithLoadingShield_HidesOnException()
		{
			var (go, shield) = NewRegisteredShield();
			try
			{
				var gate = new UniTaskCompletionSource();
				var task = gate.Task.WithLoadingShield(shield.Key);
				Assert.IsTrue(shield.IsVisible);

				gate.TrySetException(new InvalidOperationException("boom"));
				try { await task; Assert.Fail("should rethrow"); }
				catch (InvalidOperationException) { /* 期待 */ }

				Assert.IsFalse(shield.IsVisible, "例外でも確実に隠す");
			}
			finally { Cleanup(go, shield); }
		}

		[Test]
		public async Task WithLoadingShield_NoShieldRegistered_IsNoOp()
		{
			// 未登録 Key でも例外にならず素通しで待つ。
			await UniTask.CompletedTask.WithLoadingShield("not-registered");
			Assert.Pass();
		}

		static (GameObject, InputShield) NewRegisteredShield()
		{
			var go = new GameObject("shield");
			var shield = go.AddComponent<InputShield>();
			ShieldRegistry.Register(shield);
			return (go, shield);
		}

		static void Cleanup(GameObject go, InputShield shield)
		{
			ShieldRegistry.Unregister(shield);
			Object.DestroyImmediate(go);
		}

		// ---- 実 GameObject(Canvas 付き)を返すテスト用 view/handle/id ----

		sealed class CanvasView : IScreenViewInstance
		{
			readonly GameObject _go;
			public CanvasView(GameObject go) => _go = go;
			public void SetActive(bool active) { if (_go != null) _go.SetActive(active); }
			public void SetParent(Transform parent) { if (_go != null) _go.transform.SetParent(parent, worldPositionStays: false); }
			public T As<T>() where T : class => typeof(T) == typeof(GameObject) ? _go as T : null;
		}

		sealed class CanvasHandle : IScreenHandle
		{
			readonly IScreenViewInstance _view;
			public CanvasHandle(GameObject go) => _view = new CanvasView(go);
			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c) => UniTask.FromResult(_view);
			public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
		}

		sealed record CanvasViewScreenId(GameObject Go) : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new CanvasHandle(Go);
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
		}
	}
}
