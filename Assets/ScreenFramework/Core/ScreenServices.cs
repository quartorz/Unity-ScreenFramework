using UnityEngine;

namespace ScreenFramework
{
	/// <summary>
	/// プロジェクトごとに継承して共通依存を増やすための基底クラス。
	/// UseMockViews フラグだけは framework が用意する。
	/// </summary>
	public abstract class ScreenServices
	{
		public bool UseMockViews { get; }

		Transform _instantiationStagingRoot;

		protected ScreenServices(bool useMockViews)
		{
			UseMockViews = useMockViews;
		}

		/// <summary>
		/// Addressable な View / Effect を「描画されないまま」生成するための非アクティブな親 Transform。
		/// InstantiateAsync は生成物をアクティブなままシーンに出し、こちらが結果を受け取って SetActive(false) や
		/// Canvas 設定をするのは早くても次フレームになるため、その間 1 フレーム見えてしまう（チラつき）。
		/// 非アクティブ親の下で生成すれば <c>activeInHierarchy=false</c> で一度も描画されず Awake/OnEnable も走らないので、
		/// 設定を済ませてから本来の親へ移すことでチラつきを防げる。遅延生成で、通常は必要な場合にのみ作られる。
		/// シーン破棄等で消えても次アクセスで作り直す（fake-null 対応）。
		/// </summary>
		public Transform InstantiationStagingRoot
		{
			get
			{
				if (_instantiationStagingRoot == null)
				{
					var go = new GameObject("[ScreenFramework] InstantiationStaging");
					go.SetActive(false);
					_instantiationStagingRoot = go.transform;
				}
				return _instantiationStagingRoot;
			}
		}

		/// <summary>
		/// staging 親を破棄する。<see cref="ScreenNavigator.Shutdown"/> から呼ばれる。
		/// 次に <see cref="InstantiationStagingRoot"/> へアクセスされれば作り直される。
		/// </summary>
		internal void ReleaseInstantiationStagingRoot()
		{
			if (_instantiationStagingRoot == null) return;
			var go = _instantiationStagingRoot.gameObject;
			if (Application.isPlaying) Object.Destroy(go);
			else Object.DestroyImmediate(go);
			_instantiationStagingRoot = null;
		}
	}
}
