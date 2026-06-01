using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sample
{

	[RequireComponent(typeof(RectTransform))]
	[MockGenerator.GenerateViewInterfaces, MockGenerator.GenerateMockView]
	public sealed partial class DetailView : MonoBehaviour
	{
		[SerializeField] Text _label;
		[SerializeField] Button _back;

		[MockGenerator.Input] public event Action OnBackClicked;

		void Awake()
		{
			var rt = (RectTransform)transform;
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;

			var bg = gameObject.GetComponent<Image>();
			bg.color = new Color(0.5f, 0.3f, 0.15f, 1f);

			_back.onClick.AddListener(() => OnBackClicked?.Invoke());
		}

		[MockGenerator.Output]
		public void SetUserId(string userId)
		{
			if (_label != null) _label.text = $"Detail: {userId}";
		}
	}
}
