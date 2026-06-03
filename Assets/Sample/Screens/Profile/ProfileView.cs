using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sample
{
	[RequireComponent(typeof(RectTransform))]
	[MockGenerator.GenerateViewInterfaces, MockGenerator.GenerateMockView]
	public sealed partial class ProfileView : MonoBehaviour
	{
		[SerializeField] Text _userIdLabel;
		[SerializeField] Text _levelLabel;
		[SerializeField] Text _nameLabel;
		[SerializeField] Button _editNameButton;
		[SerializeField] Button _backButton;

		[MockGenerator.Input] public event Action OnEditNameClicked;
		[MockGenerator.Input] public event Action OnBackClicked;

		void Awake()
		{
			var rt = (RectTransform)transform;
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;

			var bg = gameObject.GetComponent<Image>();
			if (bg != null) bg.color = new Color(0.20f, 0.45f, 0.30f, 1f);

			if (_editNameButton != null) _editNameButton.onClick.AddListener(() => OnEditNameClicked?.Invoke());
			if (_backButton != null) _backButton.onClick.AddListener(() => OnBackClicked?.Invoke());
		}

		[MockGenerator.Output]
		public void SetUserId(string userId)
		{
			if (_userIdLabel != null) _userIdLabel.text = $"UserId: {userId}";
		}

		[MockGenerator.Output]
		public void SetLevel(int level)
		{
			if (_levelLabel != null) _levelLabel.text = $"Lv. {level}";
		}

		[MockGenerator.Output]
		public void SetName(string name)
		{
			if (_nameLabel != null) _nameLabel.text = name;
		}

		[MockGenerator.Output]
		public void SetSaving(bool saving)
		{
			if (_editNameButton != null) _editNameButton.interactable = !saving;
		}
	}
}
