using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Sample.Effects
{
	/// <summary>
	/// ガチャ結果遷移用エフェクト。引いた結果の最高 rarity に応じて演出色・滞留時間を変える。
	/// <para>
	/// プレハブ構成: ルートに CanvasGroup + Image (全画面、raycastTarget=true)。
	/// この MonoBehaviour をルートに付ける。Canvas は Override Sorting で前面へ。
	/// </para>
	/// <para>
	/// 動き: OnBeforeHide で「カバー色」を rarity に応じて選んでフェードイン →
	/// OnAfterShow で短い hold の後にフェードアウト。
	/// rarity が高いほど色が派手・hold が長くなる（高揚感）。
	/// </para>
	/// </summary>
	[RequireComponent(typeof(CanvasGroup), typeof(Image))]
	public sealed class GachaRarityEffect : ScreenEffect
	{
		[SerializeField] float _fadeInDuration = 0.35f;
		[SerializeField] float _fadeOutDuration = 0.45f;

		[Header("Rarity → 色・追加 hold (ms) のマッピング")]
		[SerializeField] Color _commonColor = new Color(1f, 1f, 1f, 1f);          // rarity 1-2
		[SerializeField] Color _rareColor   = new Color(0.31f, 0.66f, 0.87f, 1f); // rarity 3 (青)
		[SerializeField] Color _epicColor   = new Color(0.62f, 0.31f, 0.87f, 1f); // rarity 4 (紫)
		[SerializeField] Color _legendColor = new Color(1f, 0.84f, 0f, 1f);       // rarity 5+ (金)

		[SerializeField] float _holdCommon = 0.05f;
		[SerializeField] float _holdRare   = 0.20f;
		[SerializeField] float _holdEpic   = 0.40f;
		[SerializeField] float _holdLegend = 0.70f;

		CanvasGroup _group;
		Image _image;
		int _maxRarity;
		float _holdSeconds;
		bool _styleReady;

		void Awake()
		{
			_group = GetComponent<CanvasGroup>();
			_image = GetComponent<Image>();
			_group.alpha = 0f;
			_group.blocksRaycasts = true;
			_group.interactable = false;
		}

		public override UniTask OnBeforeLoad(ITransitionContext ctx, CancellationToken ct)
		{
			// load hook が走る遷移（Push 等）では早めに style を確定させておく。
			EnsureStyle(ctx);
			return UniTask.CompletedTask;
		}

		public override async UniTask OnBeforeHide(ITransitionContext ctx, CancellationToken ct)
		{
			// hook 順序は操作種別で変わり、Pop ではここが OnBeforeLoad より先に来る／load hook が来ないこともある。
			// そのため style はここでも遅延初期化して、未初期化の prefab デフォルト色でフェードする事故を防ぐ。
			EnsureStyle(ctx);
			await Fade(0f, 1f, _fadeInDuration, ct);
		}

		/// <summary>
		/// Push 側 (GachaPickerFeature) が bag に乗せた GachaResultEffectParam を読み、色・hold を確定する。
		/// 初回だけ計算し、以降の hook では使い回す。Identifier を覗きに行かないことで
		/// 「演出用に Identifier の中身を増やす」事故を防ぐ。
		/// </summary>
		void EnsureStyle(ITransitionContext ctx)
		{
			if (_styleReady) return;
			_maxRarity = 0;
			if (ctx.Reader.TryRead<GachaResultEffectParam>(out var p))
			{
				_maxRarity = p.MaxRarity;
			}
			(_image.color, _holdSeconds) = PickStyle(_maxRarity);
			_styleReady = true;
		}

		public override async UniTask OnAfterShow(ITransitionContext ctx, CancellationToken ct)
		{
			if (_holdSeconds > 0f)
			{
				await UniTask.Delay(System.TimeSpan.FromSeconds(_holdSeconds), cancellationToken: ct);
			}
			await Fade(1f, 0f, _fadeOutDuration, ct);
		}

		(Color color, float hold) PickStyle(int maxRarity)
		{
			if (maxRarity >= 5) return (_legendColor, _holdLegend);
			if (maxRarity == 4) return (_epicColor,   _holdEpic);
			if (maxRarity == 3) return (_rareColor,   _holdRare);
			return (_commonColor, _holdCommon);
		}

		async UniTask Fade(float from, float to, float duration, CancellationToken ct)
		{
			if (duration <= 0f) { _group.alpha = to; return; }
			_group.alpha = from;
			var elapsed = 0f;
			while (elapsed < duration)
			{
				ct.ThrowIfCancellationRequested();
				elapsed += Time.deltaTime;
				_group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
				await UniTask.Yield(PlayerLoopTiming.Update, ct);
			}
			_group.alpha = to;
		}
	}
}
