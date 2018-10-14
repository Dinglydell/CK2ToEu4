using PdxFile;
using System.Linq;

namespace CK2ToEU4
{
	public class CK2Revolt
	{

		public int Attacker { get; set; }
		public int Defender { get; set; }

		public int Actor { get; set; }


		public string CasusBelli { get; set; }

		public CK2Revolt(PdxSublist data)
		{
			Actor = (int)data.Sublists["casus_belli"].FloatValues["actor"].Single();
			Defender = (int)data.Sublists["casus_belli"].FloatValues["recipient"].Single();
			if (data.Sublists["casus_belli"].FloatValues.ContainsKey("thirdparty"))
			{
				Attacker = (int)data.Sublists["casus_belli"].FloatValues["thirdparty"].SingleOrDefault();
			} else
			{
				Attacker = Actor;
			}
			CasusBelli = data.Sublists["casus_belli"].KeyValuePairs["casus_belli"];
		}
	}
}