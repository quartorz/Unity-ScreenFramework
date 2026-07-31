using NUnit.Framework;
using UnityEngine;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// ScreenServices.InstantiationStagingRoot の性質。InstantiateAsync の生成物を「描画させずに」受け取る
	/// ための非アクティブ親で、これが View / Effect 生成時のチラつき防止の土台になる。
	/// </summary>
	public sealed class InstantiationStagingTests
	{
		[Test]
		public void StagingRoot_IsInactive_SoChildrenNeverRender()
		{
			var services = new TestServices();
			var root = services.InstantiationStagingRoot;

			Assert.IsNotNull(root, "staging 親は遅延生成で必ず得られる");
			Assert.IsFalse(root.gameObject.activeSelf,
				"staging 親は非アクティブ。配下に生成された prefab は activeInHierarchy=false で一度も描画されない");

			Object.DestroyImmediate(root.gameObject);
		}

		[Test]
		public void StagingRoot_IsCachedAcrossAccesses()
		{
			var services = new TestServices();
			var first = services.InstantiationStagingRoot;
			var second = services.InstantiationStagingRoot;

			Assert.AreSame(first, second, "同一インスタンスを使い回す（毎回作らない）");

			Object.DestroyImmediate(first.gameObject);
		}

		[Test]
		public void StagingRoot_IsRecreatedAfterDestroy()
		{
			var services = new TestServices();
			var first = services.InstantiationStagingRoot;
			Object.DestroyImmediate(first.gameObject);

			var recreated = services.InstantiationStagingRoot;
			Assert.IsTrue(recreated != null, "破棄後のアクセスで作り直される（fake-null 対応）");
			Assert.IsFalse(recreated.gameObject.activeSelf, "作り直したものも非アクティブ");

			Object.DestroyImmediate(recreated.gameObject);
		}
	}
}
