using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK2ToEU4
{
	class Program
	{
		static void Main(string[] args)
		{
			var keepStartDate = !args.Contains("1444");
			var save = new CK2Save("Grindia.ck2", string.Empty, keepStartDate);
			var eu4 = new Eu4World(save);
			Console.WriteLine("Done.");
			Console.Read();
		}
	}
}
