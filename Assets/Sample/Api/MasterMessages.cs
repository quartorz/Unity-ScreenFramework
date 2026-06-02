using System;

namespace Sample.Api
{
	/// <summary>
	/// 起動時に一括取得するマスタデータ全部入り。新しいマスタを足したくなったら
	/// このクラスにフィールドを追加してサーバー/タイトル側で配るだけにする。
	/// </summary>
	[Serializable]
	public class BootstrapMasterResponse
	{
		public ItemMasterResponse[] items;
	}

	[Serializable]
	public class ItemMasterResponse
	{
		public int id;
		public string code;
		public string name;
		public int rarity;
	}
}
