using System;
using System.Text;

namespace HSPI_ratgdo;

public static class Helpers {
	private const string RANDOM_STRING_CHARS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
	
	public static string RandomString(int length) {
		StringBuilder builder = new StringBuilder();
		Random random = new Random();
		
		for (int i = 0; i < length; i++) {
			int rand = random.Next(0, RANDOM_STRING_CHARS.Length - 1);
			builder.Append(RANDOM_STRING_CHARS.Substring(rand, 1));
		}

		return builder.ToString();
	}
}