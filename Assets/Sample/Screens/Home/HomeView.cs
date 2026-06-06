using UnityEngine;
using UnityEngine.UI;

namespace Sample
{
	[RequireComponent(typeof(RectTransform))]
	[MockGenerator.GenerateMockView, MockGenerator.GenerateViewInterfaces]
	public sealed partial class HomeView : MonoBehaviour
	{
		[SerializeField] Text _title;
		[MockGenerator.Input, SerializeField] SampleButton _goProfile;
		[MockGenerator.Input, SerializeField] SampleButton _goGacha;

		[MockGenerator.Output]
		public void SetTitle(string title)
		{
			if (_title != null) _title.text = title;
		}
	}
}
