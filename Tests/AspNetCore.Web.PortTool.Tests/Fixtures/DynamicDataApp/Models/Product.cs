using System.ComponentModel.DataAnnotations;

namespace LegacyDynamicData.Models
{
	[ScaffoldTable (true)]
	public class Product
	{
		[Key]
		public int Id { get; set; }
		public string Name { get; set; }
	}
}
