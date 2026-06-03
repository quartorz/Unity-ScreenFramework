using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ScreenFramework
{
	/// <summary>
	/// CanvasGroup ベースのフェード演出。
	/// Start で overlayParent の手前に半透明オーバーレイを生やして α 0→1、
	/// End で α 1→0 してから破棄する。
	/// overlayParent は Canvas 配下を指す Transform を渡すこと。
	/// </summary>
	public sealed class FadeTransition : IScreenTransitionDirector
	{
		readonly Func<Transform> _overlayParentProvider;
		readonly float _duration;
		readonly Color _color;
		readonly bool _useUnscaledTime;

		public FadeTransition(Transform overlayParent, float duration = 0.25f, Color? color = null, bool useUnscaledTime = false)
			: this(() => overlayParent, duration, color, useUnscaledTime)
		{
			if (overlayParent == null) throw new ArgumentNullException(nameof(overlayParent));
		}

		public FadeTransition(Func<Transform> overlayParentProvider, float duration = 0.25f, Color? color = null, bool useUnscaledTime = false)
		{
			_overlayParentProvider = overlayParentProvider ?? throw new ArgumentNullException(nameof(overlayParentProvider));
			_duration = Mathf.Max(0f, duration);
			_color = color ?? Color.black;
			_useUnscaledTime = useUnscaledTime;
		}

		public IScreenTransitionHandle CreateHandle() => new Handle(_overlayParentProvider, _duration, _color, _useUnscaledTime);

		sealed class Handle : IScreenTransitionHandle
		{
			readonly Func<Transform> _parentProvider;
			readonly float _duration;
			readonly Color _color;
			readonly bool _useUnscaledTime;
			GameObject _overlayGo;
			CanvasGroup _group;

			public Handle(Func<Transform> parentProvider, float duration, Color color, bool useUnscaledTime)
			{
				_parentProvider = parentProvider;
				_duration = duration;
				_color = color;
				_useUnscaledTime = useUnscaledTime;
			}

			public async UniTask Start(CancellationToken ct)
			{
				EnsureOverlay();
				_group.alpha = 0f;
				await Fade(0f, 1f, ct);
			}

			public async UniTask End(CancellationToken ct)
			{
				if (_group == null) return;
				try
				{
					await Fade(1f, 0f, ct);
				}
				finally
				{
					Cleanup();
				}
			}

			void EnsureOverlay()
			{
				if (_overlayGo != null) return;
				var parent = _parentProvider() ?? throw new InvalidOperationException("FadeTransition overlay parent is null.");

				_overlayGo = new GameObject("ScreenFramework.FadeOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
				var rt = (RectTransform)_overlayGo.transform;
				rt.SetParent(parent, worldPositionStays: false);
				rt.anchorMin = Vector2.zero;
				rt.anchorMax = Vector2.one;
				rt.offsetMin = Vector2.zero;
				rt.offsetMax = Vector2.zero;
				rt.SetAsLastSibling();

				var image = _overlayGo.GetComponent<Image>();
				image.color = _color;
				image.raycastTarget = true; // 入力遮蔽

				_group = _overlayGo.GetComponent<CanvasGroup>();
				_group.blocksRaycasts = true;
				_group.interactable = false;
			}

			async UniTask Fade(float from, float to, CancellationToken ct)
			{
				if (_duration <= 0f)
				{
					_group.alpha = to;
					return;
				}
				var elapsed = 0f;
				while (elapsed < _duration)
				{
					ct.ThrowIfCancellationRequested();
					var dt = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
					elapsed += dt;
					var t = Mathf.Clamp01(elapsed / _duration);
					_group.alpha = Mathf.Lerp(from, to, t);
					await UniTask.Yield(PlayerLoopTiming.Update, ct);
				}
				_group.alpha = to;
			}

			void Cleanup()
			{
				if (_overlayGo != null)
				{
					if (Application.isPlaying) UnityEngine.Object.Destroy(_overlayGo);
					else UnityEngine.Object.DestroyImmediate(_overlayGo);
					_overlayGo = null;
					_group = null;
				}
			}
		}
	}
}
