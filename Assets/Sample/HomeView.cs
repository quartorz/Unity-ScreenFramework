using MockGenerator;
using System;
using UnityEngine;
using UnityEngine.UI;
using static Sample.HomeView;

namespace Sample
{
	[RequireComponent(typeof(RectTransform))]
	[MockGenerator.GenerateMockView, MockGenerator.GenerateViewInterfaces]
	public sealed partial class HomeView : MonoBehaviour
	{
		[SerializeField] Text _title;
		[SerializeField] Button _goProfile;
		[SerializeField] Button _goGacha;

		[MockGenerator.Input]  public event Action OnGoProfileClicked;
		[MockGenerator.Input]  public event Action OnGoGachaClicked;

		void Awake()
		{
			// 自身に簡易 UI を生成
			var rt = (RectTransform)transform;
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;

			var bg = gameObject.GetComponent<Image>();
			bg.color = new Color(0.15f, 0.30f, 0.50f, 1f);

			if (_goProfile != null) _goProfile.onClick.AddListener(() => OnGoProfileClicked?.Invoke());
			if (_goGacha != null) _goGacha.onClick.AddListener(() => OnGoGachaClicked?.Invoke());
		}

		[MockGenerator.Output]
		public void SetTitle(string title)
		{
			if (_title != null) _title.text = title;
		}
	}
}
