using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Sample.Dialogs
{
	[RequireComponent(typeof(RectTransform))]
	[MockGenerator.GenerateViewInterfaces, MockGenerator.GenerateMockView]
	public sealed partial class InputDialogView : MonoBehaviour, IScreenAnimatedView
	{
		const float EnterDuration = 0.18f;
		const float ExitDuration  = 0.12f;
		const float StartScale    = 0.85f;

		[SerializeField] RectTransform _panel; // 拡縮 + フェードの対象。未指定なら自身
		[SerializeField] CanvasGroup _panelGroup;
		[SerializeField] Text _titleLabel;
		[SerializeField] InputField _input;
		[SerializeField] Button _okButton;
		[SerializeField] Button _cancelButton;

		[MockGenerator.Input] public event Action OnOkClicked;
		[MockGenerator.Input] public event Action OnCancelClicked;

		void Awake()
		{
			if (_okButton != null) _okButton.onClick.AddListener(() => OnOkClicked?.Invoke());
			if (_cancelButton != null) _cancelButton.onClick.AddListener(() => OnCancelClicked?.Invoke());
		}

		RectTransform Target => _panel != null ? _panel : (RectTransform)transform;

		public async UniTask PlayEnter(CancellationToken ct)
		{
			var t = Target;
			t.localScale = Vector3.one * StartScale;
			if (_panelGroup != null) _panelGroup.alpha = 0f;

			var elapsed = 0f;
			while (elapsed < EnterDuration)
			{
				ct.ThrowIfCancellationRequested();
				elapsed += Time.unscaledDeltaTime;
				var p = Mathf.Clamp01(elapsed / EnterDuration);
				var e = EaseOutBack(p);
				t.localScale = Vector3.one * Mathf.LerpUnclamped(StartScale, 1f, e);
				if (_panelGroup != null) _panelGroup.alpha = p;
				await UniTask.Yield(PlayerLoopTiming.Update, ct);
			}
			t.localScale = Vector3.one;
			if (_panelGroup != null) _panelGroup.alpha = 1f;
		}

		public async UniTask PlayExit(CancellationToken ct)
		{
			var t = Target;
			var elapsed = 0f;
			while (elapsed < ExitDuration)
			{
				ct.ThrowIfCancellationRequested();
				elapsed += Time.unscaledDeltaTime;
				var p = Mathf.Clamp01(elapsed / ExitDuration);
				var e = EaseInQuad(p);
				t.localScale = Vector3.one * Mathf.Lerp(1f, StartScale, e);
				if (_panelGroup != null) _panelGroup.alpha = 1f - p;
				await UniTask.Yield(PlayerLoopTiming.Update, ct);
			}
			if (_panelGroup != null) _panelGroup.alpha = 0f;
		}

		// オーバーシュートあり：1.70158 は標準的な係数
		static float EaseOutBack(float x)
		{
			const float c1 = 1.70158f;
			const float c3 = c1 + 1f;
			var v = x - 1f;
			return 1f + c3 * v * v * v + c1 * v * v;
		}

		static float EaseInQuad(float x) => x * x;

		[MockGenerator.Output]
		public void SetTitle(string title)
		{
			if (_titleLabel != null) _titleLabel.text = title;
		}

		[MockGenerator.Output]
		public void SetInitialText(string text)
		{
			if (_input != null) _input.text = text ?? string.Empty;
		}

		[MockGenerator.Output]
		public string GetText() => _input != null ? _input.text : string.Empty;
	}
}
