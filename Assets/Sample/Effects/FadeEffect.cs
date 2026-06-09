using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Sample.Effects
{
	/// <summary>
	/// 旧 <c>FadeTransition</c> を Effect として再実装したもの。
	/// プレハブには CanvasGroup + Image (raycastTarget=true, 全画面) を持つルートを 1 個置き、
	/// この MonoBehaviour をそこに付ける。Canvas は Override Sorting + sortingOrder で前面に持ってくる。
	/// <para>
	/// 動き: <c>OnBeforeExit</c> で α 0→1 (カバー)、<c>OnAfterEnter</c> で α 1→0 (リビール)。
	/// 旧 FadeTransition の Start / End と同じタイミングで「下層の切替を隠す」役割を担う。
	/// </para>
	/// </summary>
	[RequireComponent(typeof(CanvasGroup))]
	public sealed class FadeEffect : ScreenEffect
	{
		[SerializeField] float _duration = 0.25f;
		[SerializeField] bool _useUnscaledTime = false;

		CanvasGroup _group;

		void Awake()
		{
			_group = GetComponent<CanvasGroup>();
			_group.alpha = 0f;
			_group.blocksRaycasts = true;
			_group.interactable = false;
		}

		public override async UniTask OnBeforeExit(ITransitionContext ctx, CancellationToken ct)
		{
			await Fade(0f, 1f, ct);
		}

		public override async UniTask OnAfterEnter(ITransitionContext ctx, CancellationToken ct)
		{
			await Fade(1f, 0f, ct);
		}

		async UniTask Fade(float from, float to, CancellationToken ct)
		{
			if (_duration <= 0f)
			{
				_group.alpha = to;
				return;
			}
			_group.alpha = from;
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
	}
}
