using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sample
{
	[RequireComponent(typeof(RectTransform))]
	[MockGenerator.GenerateViewInterfaces, MockGenerator.GenerateMockView]
	public sealed partial class TitleView : MonoBehaviour
	{
		[SerializeField] Text _title;
		[SerializeField] Text _status;
		[SerializeField] Button _startButton;

		[MockGenerator.Input] public event Action OnStartClicked;

		void Awake()
		{
			var rt = (RectTransform)transform;
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;

			var bg = gameObject.GetComponent<Image>();
			if (bg != null) bg.color = new Color(0.08f, 0.10f, 0.20f, 1f);

			if (_startButton != null)
				_startButton.onClick.AddListener(() => OnStartClicked?.Invoke());
		}

		[MockGenerator.Output]
		public void SetTitle(string title)
		{
			if (_title != null) _title.text = title;
		}

		[MockGenerator.Output]
		public void SetStatus(string status)
		{
			if (_status != null) _status.text = status;
		}

		[MockGenerator.Output]
		public void SetStartButtonInteractable(bool interactable)
		{
			if (_startButton != null) _startButton.interactable = interactable;
		}
	}
}
