using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sample
{
	/// <summary>
	/// 入力遮蔽板（過去プロジェクトで "Shield" と呼んでいたもの）。自前 Canvas（overrideSorting）+
	/// GraphicRaycaster + 全画面 raycast Image（プレハブ側で用意）で、置いた sortingOrder 以下の UI への
	/// 入力を吸う。<b>シーンに手動配置</b>し、どの Shield をいつ出すかはプロジェクト側が決める
	/// （<see cref="ShieldUniTaskExtensions.WithLoadingShield(Cysharp.Threading.Tasks.UniTask,string)"/> 等）。
	/// <para>
	/// ScreenFramework のレイヤー Canvas（ScreenLayerConfig.SortingOrder）と同じ sorting 空間に置くことで、
	/// 上位レイヤー（Dialog 等）に出した Shield が下位レイヤー（Page）を Canvas 優先度で遮断できる。
	/// 配置 sortingOrder は <b>SystemDialog レイヤーより小さく</b>すること。最前面（エラーダイアログ等）まで
	/// 遮蔽すると、ダイアログを閉じられず進行不能になる。複数階層の Shield は <see cref="Key"/> で区別する。
	/// </para>
	/// <para>
	/// 表示の on/off は GameObject の active ではなく <see cref="Canvas.enabled"/> /
	/// <see cref="GraphicRaycaster.enabled"/> で行う（active を切ると <see cref="OnDisable"/> で
	/// レジストリ登録が外れてしまうため）。<see cref="Show"/>/<see cref="Hide"/> は参照カウントで多重対応。
	/// </para>
	/// <para>
	/// これは ScreenFramework 本体ではなくプロジェクト（Sample）側の実装。framework はレイヤー Canvas と
	/// sortingOrder の土台だけ提供し、どんな遮蔽板を何階層・いつ出すかはプロジェクトのポリシーとして持つ。
	/// </para>
	/// </summary>
	[RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
	public sealed class InputShield : MonoBehaviour
	{
		[Tooltip("WithLoadingShield などから参照する識別子。")]
		[SerializeField] string _key = ShieldUniTaskExtensions.DefaultLoadingKey;

		[Tooltip("この Shield の Canvas.sortingOrder。SystemDialog レイヤーより小さくすること。")]
		[SerializeField] int _sortingOrder;

		[Tooltip("起動時から表示状態にするか。")]
		[SerializeField] bool _visibleOnAwake;

		Canvas _canvas;
		GraphicRaycaster _raycaster;
		int _count;

		public string Key => _key;
		public bool IsVisible => _count > 0;

		void Awake()
		{
			_canvas = GetComponent<Canvas>();
			_raycaster = GetComponent<GraphicRaycaster>();
			_canvas.overrideSorting = true;
			_canvas.sortingOrder = _sortingOrder;
			_count = _visibleOnAwake ? 1 : 0;
			ApplyVisible(_count > 0);
		}

		void OnEnable() => ShieldRegistry.Register(this);
		void OnDisable() => ShieldRegistry.Unregister(this);

		/// <summary>参照カウントを 1 増やし、0→1 で表示する。</summary>
		public void Show()
		{
			if (_count++ == 0) ApplyVisible(true);
		}

		/// <summary>参照カウントを 1 減らし、1→0 で非表示にする。0 のときは何もしない。</summary>
		public void Hide()
		{
			if (_count > 0 && --_count == 0) ApplyVisible(false);
		}

		/// <summary>参照カウントを無視して即座に隠す（強制リセット）。</summary>
		public void ForceHide()
		{
			_count = 0;
			ApplyVisible(false);
		}

		void ApplyVisible(bool visible)
		{
			if (_canvas != null) _canvas.enabled = visible;
			if (_raycaster != null) _raycaster.enabled = visible;
		}
	}

	/// <summary>
	/// シーン上の <see cref="InputShield"/> を <see cref="InputShield.Key"/> で引けるレジストリ。
	/// InputShield が自分で OnEnable/OnDisable で登録・解除する。
	/// </summary>
	public static class ShieldRegistry
	{
		static readonly Dictionary<string, InputShield> _byKey = new();

		public static void Register(InputShield shield)
		{
			if (shield == null || string.IsNullOrEmpty(shield.Key)) return;
			_byKey[shield.Key] = shield;
		}

		public static void Unregister(InputShield shield)
		{
			if (shield == null || string.IsNullOrEmpty(shield.Key)) return;
			// 同じ Key で別インスタンスに差し替わっている場合は消さない
			if (_byKey.TryGetValue(shield.Key, out var current) && ReferenceEquals(current, shield))
				_byKey.Remove(shield.Key);
		}

		/// <summary>登録された Shield を返す。無ければ null。</summary>
		public static InputShield Get(string key)
			=> key != null && _byKey.TryGetValue(key, out var s) ? s : null;
	}
}
