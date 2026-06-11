using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sample;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// Sample 側の入力遮蔽板 InputShield / ShieldRegistry / WithLoadingShield の挙動を検証する。
	/// Tests は EditMode アセンブリのため MonoBehaviour の Awake/OnEnable は自動実行されない。
	/// よってレジストリ登録は手動で行い、可視判定は IsVisible（参照カウント）で見る
	/// （Canvas/GraphicRaycaster の enabled トグルは Awake 後にしか効かないが、参照カウントは Awake 非依存）。
	/// </summary>
	public sealed class ShieldTests
	{
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
	}
}
