using UnityEngine;

namespace ScreenFramework
{
	/// <summary>
	/// 既定の IScreenContainer 実装。空 GameObject に貼って Root として使う。
	/// このコンテナ自身は Canvas を持たず、描画順は各画面プレハブの Canvas に
	/// Sorting Layer / Order を振ることで制御する（<see cref="SortingLayerId"/> / <see cref="BaseOrder"/> / <see cref="OrderStep"/>）。
	/// </summary>
	public sealed class ScreenContainer : MonoBehaviour, IScreenContainer
	{
		[SerializeField] Camera _renderCamera;
		[SerializeField] string _sortingLayerName = "Default";
		[SerializeField] int _baseOrder = 0;
		[SerializeField] int _orderStep = 10;

		void OnValidate()
		{
			if (string.IsNullOrEmpty(_sortingLayerName)) _sortingLayerName = "Default";
			if (_orderStep <= 0) _orderStep = 10;
		}

		void Awake()
		{
			if (_renderCamera == null)
				Debug.LogError($"[{nameof(ScreenContainer)}] Render camera is not assigned on '{name}'. UI may not render.");
		}

		public Transform Root => transform;
		public Camera RenderCamera => _renderCamera;
		public int SortingLayerId => SortingLayer.NameToID(_sortingLayerName);
		public int BaseOrder => _baseOrder;
		public int OrderStep => _orderStep;
	}
}
