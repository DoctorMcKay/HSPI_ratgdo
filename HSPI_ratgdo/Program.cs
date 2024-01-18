namespace HSPI_ratgdo;

internal class Program {
	public static void Main(string[] args) {
		HSPI plugin = new HSPI();
		plugin.Connect(args);
	}
}