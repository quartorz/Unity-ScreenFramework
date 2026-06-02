namespace Sample
{
	[DataTableGenerator.DataTable("Id")]
	[DataTableGenerator.DataTableIndex("Code")]
	[DataTableGenerator.DataTableSort("Rarity:desc", "Id")]
	public class ItemMaster
	{
		public int Id { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public int Rarity { get; set; }
	}
}
