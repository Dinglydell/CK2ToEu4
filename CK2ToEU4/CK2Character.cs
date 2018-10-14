using CK2Helper;
using PdxFile;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK2ToEU4
{
	public class CK2Character: CK2CharacterBase
	{
		private int totalStrengthCache;
		public CK2Character(CK2World world, PdxSublist data) : base(world, data) { }


		public void AddStrength(int amt)
		{
			totalStrengthCache += amt;
		}
		//TODO: sort this crap out it's a massive hack
		public int GetTotalStrength()
		{
			return totalStrengthCache;
			//if (totalStrengthCache != -1)
			//{
			//	return totalStrengthCache;
			//}
			//totalStrengthCache = 0;
			//
			//foreach (var title in Titles)
			//{
			//	if (title.Rank == TitleRank.county)
			//	{
			//		((CK2Save) World).Eu4World.pro
			//		title.Province.
			//	}
			//	//totalStrengthCache += title.GetTitleStrength();
			//}
			//
			////foreach(var character in World.CK2Characters)
			////{
			////	if (IsLiegeOf(character.Value))
			////	{
			////		totalStrengthCache += character.Value.GetTotalStrength();
			////	}
			////}
			//
			//return totalStrengthCache;
		}
	}
}
