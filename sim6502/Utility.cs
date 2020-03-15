using System;
using System.IO;
using System.Text.RegularExpressions;
using NCalc2;
using NLog;

namespace sim6502
{
	public static class Utility
	{
		/// <summary>
		/// Convert an integer to a hex string
		/// </summary>
		/// <param name="number">The number to convert</param>
		/// <returns></returns>
		public static string ToHex(this int number)
		{
			var numDigits = 1;
			if (number >= 16)
				numDigits++;
			if (number >= 256)
				numDigits++;
			if (number >= 4096)
				numDigits++;
			
			var hex = number.ToString($"X{numDigits}").ToLower();
			return $"${hex}";
		}
		
		/// <summary>
		/// Parse a string and see if we can get an integer out of it
		/// </summary>
		/// <param name="number">The thing to parse</param>
		/// <returns>The thing as an integer</returns>
		public static int ParseNumber(this string number)
		{
			var retval = 0;
			if (number.StartsWith("$"))
			{
				number = number.Replace("$", "0x");
				retval = Convert.ToInt32(number, 16);
			}
			else if(number.StartsWith("0x"))
			{
				retval = Convert.ToInt32(number, 16);
			}
			else
			{
				retval = int.Parse(number);
			}

			return retval;
		}

		/// <summary>
		/// Given an assembled .prg file, return its load address. This will be the file's
		/// first two bytes in little endian format.
		/// </summary>
		/// <param name="filename">The path to the file to get the load address for. Must be a .prg</param>
		/// <returns>16-bit load address</returns>
		public static int GetProgramLoadAddress(string filename)
		{
			var buffer = new byte[2];
			using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
			{
				fs.Read(buffer, 0, 2);
			}

			return GetProgramLoadAddress(buffer);
		}

		/// <summary>
		/// Given a program as a byte array, return its load address
		/// </summary>
		/// <param name="program">The program expressed as a byte array</param>
		/// <returns>The 16-bit load address of the program</returns>
		public static int GetProgramLoadAddress(byte[] program)
		{
			return program[1] * 256 + program[0];
		}

		/// <summary>
		/// Allow us to use expressions in our test yaml which include things like symbol references
		/// </summary>
		/// <param name="expression">The expression to parse</param>
		/// <param name="sf">A fully loaded symbol file</param>
		/// <returns>The expression's integer value</returns>
		public static int EvaluateExpression(string expression, SymbolFile sf)
		{
			foreach (Match match in Regex.Matches(expression, "{([0-9a-zA-Z_]+)}"))
			{
				var symbol = match.Groups[1].Value;
				var value = sf.SymbolToAddress(symbol);
				var capture = match.Groups[0].Value;
				
				expression = expression.Replace(capture, value.ToString());
			}
			
			var expr = new Expression(expression);
			return Convert.ToInt32(expr.Evaluate());
		}
	}
}
