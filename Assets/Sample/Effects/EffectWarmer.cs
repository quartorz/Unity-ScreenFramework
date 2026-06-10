using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Sample.Effects
{
	/// <summary>
	/// Effect prefab の Addressables bundle を事前ロードして常駐させ、遷移時の
	/// <c>InstantiateAsync</c> をディスク/解凍なしの速い経路にするための簡易ウォーマ。
	/// <para>
	/// プールはしない（インスタンスは従来通り遷移ごとに生成）。ここでやるのは「アセット常駐」だけ。
	/// 保持中の <see cref="AsyncOperationHandle"/> が bundle の参照カウントを握り続けるので、
	/// <see cref="ReleaseAll"/> するまでアンロードされない。
	/// </para>
	/// <para>
	/// 全行を warm すると本末転倒なので、起動時はデフォルト行（<see cref="WarmDefaults"/>）だけ、
	/// あるいはセクションに入る時にそのセクションで使う行だけを <see cref="Warm"/> し、出る時に
	/// <see cref="ReleaseAll"/> する運用を想定。
	/// </para>
	/// <para>
	/// ライフタイムは呼び出し側持ち。warm した分は必ず <see cref="ReleaseAll"/> で解放すること。
	/// <see cref="Warm"/> がキャンセル/失敗で中断しても、起動済みハンドルは登録済みなので
	/// <see cref="ReleaseAll"/> がまとめて回収する（中途半端な常駐は残さない）。
	/// </para>
	/// </summary>
	public sealed class EffectWarmer
	{
		// RuntimeKey 単位で保持。AssetReference インスタンス内部のハンドルは触らない（Instantiate 経路と独立させる）。
		readonly Dictionary<object, AsyncOperationHandle<GameObject>> _handles = new();

		/// <summary>
		/// 指定した Effect prefab 群をロードして常駐させる。既に warm 済みのキーは飛ばす。
		/// 失敗した行はログを出してスキップ（warm は best-effort で、本筋の遷移を妨げない）。
		/// <para>
		/// <paramref name="ct"/> でキャンセルされると待機中に OCE を投げて中断するが、その時点で
		/// 起動済みの全ハンドルは <c>_handles</c> に登録済みなので、<see cref="ReleaseAll"/> を呼べば
		/// （ロード中のものも含めて）まとめて回収される。中途半端に常駐したまま漏れることはない。
		/// </para>
		/// </summary>
		public async UniTask Warm(IEnumerable<AssetReferenceGameObject> prefabs, CancellationToken ct = default)
		{
			if (prefabs == null) return;

			var started = new List<object>();
			foreach (var prefab in prefabs)
			{
				if (prefab == null || !prefab.RuntimeKeyIsValid()) continue;
				var key = prefab.RuntimeKey;
				if (_handles.ContainsKey(key)) continue;

				// 先に全部キックして _handles に登録してから待つ（並列ロード兼、キャンセル時の取りこぼし防止）。
				_handles[key] = Addressables.LoadAssetAsync<GameObject>(key);
				started.Add(key);
			}

			foreach (var key in started)
			{
				var handle = _handles[key];
				while (!handle.IsDone)
				{
					await UniTask.Yield(PlayerLoopTiming.Update, ct);
				}
				if (handle.Status != AsyncOperationStatus.Succeeded)
				{
					Debug.LogWarning($"[Sample] EffectWarmer: warm failed for key '{key}'. Skipping.");
					if (handle.IsValid()) Addressables.Release(handle);
					_handles.Remove(key);
				}
			}
		}

		/// <summary>
		/// Registry のデフォルト行（from/to ともに wildcard = <c>(null, null)</c>）の Effect だけを warm する。
		/// ほぼ全遷移の受け皿になる 1〜数個だけを常駐させたい起動時用。
		/// </summary>
		public UniTask WarmDefaults(EffectRegistry registry, CancellationToken ct = default)
		{
			if (registry == null) return UniTask.CompletedTask;

			var defaults = new List<AssetReferenceGameObject>();
			foreach (var row in registry.Rows)
			{
				if (row.From == null && row.To == null && row.EffectPrefab != null)
					defaults.Add(row.EffectPrefab);
			}
			return Warm(defaults, ct);
		}

		/// <summary>
		/// 握っている bundle を全て解放する。以降の遷移は（再 warm しない限り）初回ロードのコストに戻る。
		/// ロード中のハンドルも解放対象（Addressables 側で完了後に decref される）なので、
		/// 中断した <see cref="Warm"/> の後始末もこれ 1 回で済む。
		/// </summary>
		public void ReleaseAll()
		{
			foreach (var handle in _handles.Values)
			{
				if (handle.IsValid()) Addressables.Release(handle);
			}
			_handles.Clear();
		}
	}
}
