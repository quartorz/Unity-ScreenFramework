using UnityEngine;
using UnityEngine.UI;

namespace Sample
{
    [MockGenerator.GenerateViewInterfaces, MockGenerator.GenerateMockView]
	public partial class SampleButton : MonoBehaviour
    {
        [SerializeField] Button _button;

        [MockGenerator.Input] public event System.Action OnClicked;
		public bool Interactable
		{
			[MockGenerator.Output]
			set
			{
				if (_button != null) _button.interactable = value;
			}
		}
		public string Text
		{
			[MockGenerator.Output]
			set
			{
				if (_button != null)
				{
					var text = _button.GetComponentInChildren<Text>();
					if (text != null) text.text = value;
				}
			}
		}

		void Awake()
		{
			if (_button != null) _button.onClick.AddListener(() => OnClicked?.Invoke());
		}
	}
}
