using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Sample.Dialogs
{
	/// <summary>
	/// 汎用メッセージダイアログ。タイトル + 本文 + 動的なN個のボタン + オプションの右上 X ボタン。
	/// ボタンは <see cref="_buttonTemplate"/> を SetActive(false) で隠した状態でプレハブに置いておき、
	/// SetButtons 時に複製して並べる。
	/// </summary>
	[RequireComponent(typeof(RectTransform))]
	[MockGenerator.GenerateViewInterfaces, MockGenerator.GenerateMockView]
	public sealed partial class MessageDialogView : MonoBehaviour, IScreenAnimatedView
	{
		const float EnterDuration = 0.18f;
		const float ExitDuration = 0.12f;
		const float StartScale = 0.85f;

		[SerializeField] RectTransform _panel;
		[SerializeField] CanvasGroup _panelGroup;
		[SerializeField] Text _titleLabel;
		[SerializeField] Text _messageLabel;
		[SerializeField] Button _buttonTemplate; // hidden template, cloned per button
		[SerializeField] Transform _buttonRow;   // parent for cloned buttons (e.g. HorizontalLayoutGroup)
		[SerializeField] Button _closeButton;    // 右上 X

		readonly List<Button> _spawnedButtons = new List<Button>();

		[MockGenerator.Input] public event Action<int> OnButtonClicked;
		[MockGenerator.Input] public event Action OnCloseClicked;

		void Awake()
		{
			if (_buttonTemplate != null) _buttonTemplate.gameObject.SetActive(false);
			if (_closeButton != null) _closeButton.onClick.AddListener(() => OnCloseClicked?.Invoke());
		}

		RectTransform Target => _panel != null ? _panel : (RectTransform)transform;

		[MockGenerator.Output]
		public void SetTitle(string title)
		{
			if (_titleLabel != null) _titleLabel.text = title;
		}

		[MockGenerator.Output]
		public void SetMessage(string message)
		{
			if (_messageLabel != null) _messageLabel.text = message;
		}

		[MockGenerator.Output]
		public void SetButtons(string[] labels)
		{
			ClearButtons();
			if (_buttonTemplate == null || labels == null) return;
			var parent = _buttonRow != null ? _buttonRow : _buttonTemplate.transform.parent;
			for (var i = 0; i < labels.Length; i++)
			{
				var index = i; // capture
				var btn = Instantiate(_buttonTemplate, parent);
				btn.gameObject.SetActive(true);
				btn.onClick.AddListener(() => OnButtonClicked?.Invoke(index));
				var label = btn.GetComponentInChildren<Text>();
				if (label != null) label.text = labels[i];
				_spawnedButtons.Add(btn);
			}
		}

		[MockGenerator.Output]
		public void SetCloseButtonVisible(bool visible)
		{
			if (_closeButton != null) _closeButton.gameObject.SetActive(visible);
		}

		void ClearButtons()
		{
			foreach (var b in _spawnedButtons)
			{
				if (b != null) Destroy(b.gameObject);
			}
			_spawnedButtons.Clear();
		}

		void OnDestroy()
		{
			ClearButtons();
		}

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

		static float EaseOutBack(float x)
		{
			const float c1 = 1.70158f;
			const float c3 = c1 + 1f;
			var v = x - 1f;
			return 1f + c3 * v * v * v + c1 * v * v;
		}

		static float EaseInQuad(float x) => x * x;
	}
}
