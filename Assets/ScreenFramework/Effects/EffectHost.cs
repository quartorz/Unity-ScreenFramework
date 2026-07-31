using System.Collections.Generic;
using UnityEngine;

namespace ScreenFramework
{
	/// <summary>
	/// 遷移演出（Effect）が乗る共有オーバーレイ。Effect prefab を生成する親 Transform と、
	/// その Canvas に流し込む描画カメラ / Sorting Layer を保持し、走る Effect ごとに
	/// 重複しない Sorting Order を採番する。
	///
	/// 同じ描画の高さに属する複数レイヤー（例: Page と Dialog）は<b>同一インスタンスを共有</b>でき、
	/// その場合は host が order を一元採番するため、同時に走る Effect 同士の order 衝突が起きない
	/// （レイヤー同士は互いを知らないので、共有 host だけが調停できる）。
	/// 描画の高さが異なるレイヤー（例: Page/Dialog の上に出る SystemDialog）には別インスタンスを割り当てる。
	/// </summary>
	public interface IEffectHost
	{
		/// <summary>Effect prefab を Instantiate する親 Transform。</summary>
		Transform Root { get; }

		/// <summary>Effect Canvas を Render Mode = Screen Space - Camera で描画するためのカメラ。</summary>
		Camera RenderCamera { get; }

		/// <summary>Effect Canvas に設定する Sorting Layer の ID。</summary>
		int SortingLayerId { get; }

		/// <summary>
		/// 走り始める Effect 1 つに、現在走っている他の Effect と重複しない Sorting Order を割り当てる。
		/// 共有 host では複数レイヤーから呼ばれても互いに被らない値を返す。
		/// 返り値は対応する Effect の終了時に <see cref="ReleaseOrder"/> へそのまま渡して返却すること。
		/// </summary>
		int LeaseOrder();

		/// <summary><see cref="LeaseOrder"/> で借りた order を返却し、その枠を再利用可能にする。</summary>
		void ReleaseOrder(int order);
	}

	/// <summary>
	/// 既定の <see cref="IEffectHost"/> 実装。Effect 用の高さに置いた空 GameObject に貼って使う。
	/// order は、貸出中がある間は最大貸出値より大きい <c>baseOrder + k * orderStep</c> を払い出し、
	/// 貸出中がない場合は <c>baseOrder</c> から再利用する。
	/// </summary>
	public sealed class EffectHost : MonoBehaviour, IEffectHost
	{
		[SerializeField] Camera _renderCamera;
		[SerializeField] string _sortingLayerName;
		[SerializeField] int _baseOrder = 0;
		[SerializeField] int _orderStep = 10;

		readonly HashSet<int> _leased = new HashSet<int>();

#if UNITY_EDITOR
		void OnValidate()
		{
			if (string.IsNullOrEmpty(_sortingLayerName)) _sortingLayerName = "Effect";
			if (_orderStep <= 0) _orderStep = 10;
		}
#endif

		void Awake()
		{
			if (_renderCamera == null)
				Debug.LogError($"[{nameof(EffectHost)}] Render camera is not assigned on '{name}'. Effects may not render.");
		}

		public Transform Root => transform;
		public Camera RenderCamera => _renderCamera;
		public int SortingLayerId => SortingLayer.NameToID(_sortingLayerName);

		public int LeaseOrder()
		{
			if (_leased.Count == 0)
			{
				_leased.Add(_baseOrder);
				return _baseOrder;
			}

			var nextOrder = _baseOrder;
			foreach (var leasedOrder in _leased)
			{
				if (leasedOrder >= nextOrder) nextOrder = leasedOrder + _orderStep;
			}

			_leased.Add(nextOrder);
			return nextOrder;
		}

		public void ReleaseOrder(int order) => _leased.Remove(order);
	}
}
