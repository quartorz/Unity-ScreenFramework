using UnityEngine;

namespace ScreenFramework
{
	/// <summary>
	/// 既定の IScreenContainer 実装。Canvas や空 GameObject に貼って Root として使う。
	/// </summary>
	public sealed class ScreenContainer : MonoBehaviour, IScreenContainer
	{
		public Transform Root => transform;
	}
}
