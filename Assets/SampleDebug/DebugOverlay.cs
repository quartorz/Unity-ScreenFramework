using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;

namespace Sample.Debug
{
	/// <summary>
	/// デバッグ操作用の IMGUI オーバーレイ。実機の IS_DEBUG ビルドでも画面を見ながら操作できる。
	/// 画面ピッカー（<see cref="DebugScreenIds"/>）と通信シナリオ操作
	/// （<see cref="DebugScenarioState"/> の遅延・失敗注入）を同居させる。
	/// <see cref="DebugBootstrap"/> が AddComponent して <see cref="Initialize"/> を呼ぶ。
	/// </summary>
	public sealed class DebugOverlay : MonoBehaviour
	{
		DebugScenarioState _scenario;
		bool _open;
		bool _navigating;
		Vector2 _scroll;

		public void Initialize(DebugScenarioState scenario)
		{
			_scenario = scenario;
		}

		void OnGUI()
		{
			if (_scenario == null) return;

			// 実機の高解像度でも操作できるよう、画面高さ基準でスケールする。
			var scale = Mathf.Max(1f, Screen.height / 800f);
			GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
			var width = Screen.width / scale;

			if (!_open)
			{
				if (GUI.Button(new Rect(width - 70, 5, 65, 28), "DEBUG"))
				{
					_open = true;
				}
				return;
			}

			var rect = new Rect(width - 250, 5, 245, 420);
			GUILayout.BeginArea(rect, GUI.skin.box);
			GUILayout.BeginHorizontal();
			GUILayout.Label("DEBUG");
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("×", GUILayout.Width(28)))
			{
				_open = false;
			}
			GUILayout.EndHorizontal();

			_scroll = GUILayout.BeginScrollView(_scroll);
			DrawScreenPicker();
			GUILayout.Space(8);
			DrawScenario();
			GUILayout.EndScrollView();
			GUILayout.EndArea();
		}

		void DrawScreenPicker()
		{
			GUILayout.Label("--- 画面 ---");
			GUI.enabled = !_navigating;
			foreach (var entry in DebugScreenIds.Entries)
			{
				if (GUILayout.Button(entry.Label))
				{
					NavigateAsync(entry).Forget();
				}
			}
			GUI.enabled = true;
		}

		void DrawScenario()
		{
			GUILayout.Label("--- 通信 ---");
			GUILayout.Label($"遅延: {_scenario.DelayMs} ms");
			_scenario.DelayMs = (int)GUILayout.HorizontalSlider(_scenario.DelayMs, 0, 2000);

			GUILayout.Space(4);
			GUILayout.Label(_scenario.FailNext
				? $"次の通信を失敗させる: {_scenario.FailureKind}"
				: "次の通信を失敗させる: なし");
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("サーバー")) Arm(DebugFailureKind.ServerError);
			if (GUILayout.Button("ネット")) Arm(DebugFailureKind.Network);
			if (GUILayout.Button("タイムアウト")) Arm(DebugFailureKind.Timeout);
			GUILayout.EndHorizontal();
			if (_scenario.FailNext && GUILayout.Button("解除"))
			{
				_scenario.FailNext = false;
			}
		}

		void Arm(DebugFailureKind kind)
		{
			_scenario.FailureKind = kind;
			_scenario.FailNext = true;
		}

		async UniTaskVoid NavigateAsync(DebugScreenEntry entry)
		{
			_navigating = true;
			try
			{
				// 開いているダイアログを片付けてから Page スタックを丸ごと組み直す。
				await ScreenNavigator.SystemDialog.DismissAll();
				await ScreenNavigator.Dialog.DismissAll();

				var route = entry.Route();
				await ScreenNavigator.Page.Reset(route[0]);
				for (var i = 1; i < route.Length; i++)
				{
					await ScreenNavigator.Page.Push(route[i]);
				}
			}
			finally
			{
				_navigating = false;
			}
		}
	}
}
