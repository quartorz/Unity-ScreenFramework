using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sample
{
	/// <summary>
	/// ガチャ結果画面。受け取ったアイテムを一覧で表示する。
	/// </summary>
	[RequireComponent(typeof(RectTransform))]
	[MockGenerator.GenerateMockView, MockGenerator.GenerateViewInterfaces]
	public sealed partial class GachaResultView : MonoBehaviour
	{
		[SerializeField] Text _titleLabel;
		[SerializeField] Transform _itemRow;       // parent for spawned item rows (e.g. VerticalLayoutGroup)
		[SerializeField] GameObject _itemTemplate; // hidden row template; 1st child Text に名前、2nd Text に rarity
		[SerializeField] Button _backButton;

		readonly List<GameObject> _spawned = new List<GameObject>();

		[MockGenerator.Input] public event Action OnBackClicked;

		void Awake()
		{
			if (_itemTemplate != null) _itemTemplate.SetActive(false);
			if (_backButton != null) _backButton.onClick.AddListener(() => OnBackClicked?.Invoke());
		}

		[MockGenerator.Output] public void SetTitle(string title)
		{
			if (_titleLabel != null) _titleLabel.text = title;
		}

		[MockGenerator.Output] public void SetItems(string[] names, int[] rarities)
		{
			ClearItems();
			if (_itemTemplate == null || names == null) return;
			var parent = _itemRow != null ? _itemRow : _itemTemplate.transform.parent;
			for (var i = 0; i < names.Length; i++)
			{
				var go = Instantiate(_itemTemplate, parent);
				go.SetActive(true);
				var texts = go.GetComponentsInChildren<Text>(true);
				if (texts.Length > 0) texts[0].text = names[i];
				if (texts.Length > 1) texts[1].text = new string('★', Mathf.Clamp(rarities[i], 1, 5));
				_spawned.Add(go);
			}
		}

		void ClearItems()
		{
			foreach (var go in _spawned)
			{
				if (go != null) Destroy(go);
			}
			_spawned.Clear();
		}

		void OnDestroy() => ClearItems();
	}
}
