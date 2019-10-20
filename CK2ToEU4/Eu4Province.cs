using Eu4Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK2Helper;
using PdxFile;

namespace CK2ToEU4
{
	public class Eu4Province : Eu4ProvinceBase
	{
		public float TotalTech
		{
			get
			{
				return EconTech + CultureTech + MilTech;
			}
		}

		public float EconTech { get; set; }
		public float CultureTech { get; set; }
		public float MilTech { get; set; }

		public string Revolt { get; set; }
		public string RevoltLeader { get; private set; }
        public List<CK2Title> CK2Titles { get; private set; }
        public int RevoltArmy { get; set; }

		//used to calcuate integer value
		private float baseTax;
		private float baseProduction;
		private float baseManpower;
		private float fortLevel;

		public float CentreOfTradeWeight { get; set; }
		public Eu4Province(Eu4World world, int ID, PdxSublist history)
		{
			this.World = world;
			this.ProvinceID = ID;
			Cores = new List<Eu4CountryBase>();
			Modifiers = new List<string>();
			LatentTradeGoods = new List<string>();
			if (history.KeyValuePairs.ContainsKey("trade_goods"))
			{
				TradeGood = history.KeyValuePairs["trade_goods"];
			}
			history.Sublists.ForEach("latent_trade_goods", (sub =>
			{
				LatentTradeGoods.AddRange(sub.Values);
			}));
            if (history.FloatValues.ContainsKey("center_of_trade"))
            {
                CentreOfTradeWeight += 0.25f * history.GetFloat("center_of_trade");
            }
			history.Sublists.ForEach("add_permanent_province_modifier", (sub) =>
			{
				var mod = sub.KeyValuePairs["name"];
				if (mod.Contains("estuary_modifier"))
				{
					CentreOfTradeWeight += 0.25f;
				}
				Modifiers.Add(mod);
				
			});
			
		}

		public Eu4World World { get; private set; }
		public bool IsHRE { get; private set; }
		

		internal void Initialise(List<CK2Title> ck2Titles)
		{
            CK2Titles = ck2Titles;
			RevoltArmy = -1;
			baseTax = 1;
			baseProduction = 1;
			baseManpower = 1;
			fortLevel = 0;


			var religions = new Dictionary<Eu4Religion, int>();
			var cultures = new Dictionary<Eu4Culture, int>();
			var countyTitles = ck2Titles.Where(t => t.Rank == TitleRank.county).Count();
			var revoltWeight = 0;
			CK2CharacterBase revolter = null;
			ck2Titles.ForEach(title =>
			{
				if (title.Rank == TitleRank.county)
				{
					if (!IsHRE)
					{
						CheckLiege(title);
					}


					EconTech += title.Province.EconTech;
					CultureTech += title.Province.CultureTech;
					MilTech += title.Province.MilTech;
					// todo: culture splitting
					if (title.Province.Religion != null)
					{
						CountRelCulture(religions, World.MapReligion(title.Province.Religion));
					}
					if (title.Province.Culture != null)
					{
						CountRelCulture(cultures, World.MapCulture(title.Province));
					}
					//baronies
					foreach (var barony in title.Province.BaronTitles)
					{
						CalcEffects(barony, 1f / (countyTitles));

					}
				}
				//CalcCountryEffects(title);
				CalcEffects(title, 1f / countyTitles);
				if (title.IsInRevolt())
				{
					revoltWeight++;
					revolter = title.GetRevolt().Holder;
					if (revolter.DoesDirectlyOwn(title))
					{
						RevoltArmy = 0;
					}
				}
				else
				{
					revoltWeight--;
				}

			});
			if(revoltWeight > 0)
			{
				ControllerTag = "REB";
				var revolt = World.CK2World.CK2Revolts[revolter.ID];
				Revolt = World.RevoltMapper[revolt.CasusBelli];
				var leader = World.CK2World.CK2Characters[revolt.Actor];
				var surname = leader.DynastyID == 0 ? string.Empty : World.CK2World.CK2Dynasties[leader.DynastyID].Name;
				RevoltLeader = $"{leader.Name} {surname}";
			}
			EconTech /= countyTitles;
			CultureTech /= countyTitles;
			MilTech /= countyTitles;
			Religion = religions.Count == 0 ? null : religions.OrderByDescending(r => r.Value).First().Key.Name;

			Culture = cultures.Count == 0 ? null : cultures.OrderByDescending(r => r.Value).First().Key.Name;

		
			BaseTax = (int)baseTax;
			BaseProduction = (int)baseProduction;
			BaseManpower = (int)baseManpower;
			ck2Titles.ForEach(t =>
			{
				AddStrength(t);
			});
			FortLevel = (int)fortLevel;





		}

		private void AddStrength(CK2Title t, CK2CharacterBase lastHolder = null)
		{
			if (t?.Holder != null)
			{
				if (!t.IsRevolt && t.Holder != lastHolder)
				{
					((CK2Character)t.Holder).AddStrength(Development);
				}
				AddStrength(t.LiegeTitle, t.Holder);
			}
		}

		private void AddToFormable(CK2Title t)
		{
			if (World.Formables.ContainsKey(t))
			{
				World.Formables[t].Provinces.Add(this);
			}
			if (t.DejureLiegeTitle != null)
			{
				AddToFormable(t.DejureLiegeTitle);
			}
		}
		public void PostInitialise(List<CK2Title> ck2Titles)
		{
			var holder = ck2Titles.Where(t => t.Holder != null).GroupBy(t => t.Holder).OrderBy(group => group.Count()).First().Key;
			//If the province is either: the same relgion as the owner, a primary or accepted culture, or in the dejure territory of the owner then it is a core.
			Cores.AddRange(World.GetCountriesFromCharacter(holder).Where(c => c.Religion == Religion || c.PrimaryCulture == Culture || c.AcceptedCultures.Contains(Culture) || World.Cultures[c.PrimaryCulture].Group.Cultures.Any(cul => cul.Name == Culture) || ck2Titles.Any(t => c.IsDejure(t))));// c.CountryTag != "KHA"));
			Owner = World.GetIndependentCountryFromCharacter((CK2Character)holder);
			if(Cores.Count == 0)
			{
				Cores.Add(Owner);
			}
			
			if(ControllerTag == null)
			{
				ControllerTag = Owner.CountryTag;
			}
			//add to owner numholdings
			ck2Titles.ForEach(title =>
			{
				AddToFormable(title);
				CalcCountryEffects(title);
				if (title.Rank == TitleRank.county)
				{
					title.Province.BaronTitles.ForEach(barony => CalcCountryEffects(barony));
				}
			});
		}

		private void CalcCountryEffects(CK2Title title)
		{


			if (title.Rank == TitleRank.county)
			{
				((Eu4Country)Owner).NumProvinces++;
				var provCountryEffects = World.CountryEffects.Sublists["province"];

                //values
                if (provCountryEffects.Sublists.ContainsKey("values"))
                {
                    var values = provCountryEffects.Sublists["values"];
                    if (values.Sublists.ContainsKey("technology"))
                    {
                        ((Eu4Country)Owner).CalcEffects(values.Sublists["technology"], TotalTech);
                    }
                } 
                //CalcEffects(values.Sublists["technology"], TotalTech * multiplier);

                var provCountryModifiers = provCountryEffects.Sublists["modifiers"];
				var prov = title.Province;
				foreach (var mod in prov.Modifiers)
				{
					if (provCountryModifiers.Sublists.ContainsKey(mod))
					{
						((Eu4Country)Owner).CalcEffects(provCountryModifiers.Sublists[mod]);
					}
				}
				if (title.Province.Hospital)
				{
					var provCountryHospital = provCountryEffects.Sublists["hospital"];
					((Eu4Country)Owner).CalcEffectsProvince(provCountryHospital);

					foreach (var build in prov.HospitalBuildings)
					{
						if (provCountryHospital.Sublists.ContainsKey(build))
						{
							((Eu4Country)Owner).CalcEffectsProvince(provCountryHospital.Sublists[build]);
						}
					}
				}
			}
			else if (title.Rank == TitleRank.barony)
			{
				((Eu4Country)Owner).NumHoldings++;
				var holdingCountryEffects = World.CountryEffects.Sublists["holding"];
				var buildingEffects = holdingCountryEffects.Sublists["buildings"];
				if (holdingCountryEffects.Sublists.ContainsKey(title.Type))
				{
					((Eu4Country)Owner).CalcEffectsHolding(holdingCountryEffects.Sublists[title.Type]);
				}
				foreach (var build in title.Buildings)
				{
					if (buildingEffects.Sublists.ContainsKey(build.Type))
					{
						((Eu4Country)Owner).CalcEffectsHolding(buildingEffects.Sublists[build.Type]);
					}
				}

			}
		}

		private void CheckLiege(CK2Title title)
		{
			if (title.DejureLiegeTitle == World.HRE || title.LiegeTitle == World.HRE)
			{
				IsHRE = true;
			}
			if (title.LiegeTitle != null)
			{
				CheckLiege(title.LiegeTitle);
			}
			if (title.DejureLiegeTitle != null)
			{
				CheckLiege(title.DejureLiegeTitle);
			}
		}

		private void CountRelCulture<T>(Dictionary<T, int> dict, T key)
		{
			if (!dict.ContainsKey(key))
			{
				dict[key] = 0;
			}
			dict[key]++;
		}

		private void CalcEffects(CK2Title title, float multiplier)
		{
			if (title.Rank == TitleRank.county)
			{
				var countyList = World.ProvinceEffects.Sublists["county"];
                //values
                var values = countyList.Sublists["values"];
                CalcEffects(values.Sublists["technology"], TotalTech * multiplier);

                // hospital
                var hospital = countyList.Sublists["hospital"];
				if (title.Province.Hospital)
				{
					CalcEffects(hospital, multiplier);
					//hospital buildings
					foreach (var building in title.Province.HospitalBuildings)
					{
						if (hospital.Sublists.ContainsKey(building))
						{
							CalcEffects(hospital.Sublists[building], multiplier);
						}
					}
				}

				//modifiers
				var modifiers = countyList.Sublists["modifiers"];
				foreach (var modifier in title.Province.Modifiers)
				{
					if (modifiers.Sublists.ContainsKey(modifier))
					{
						CalcEffects(modifiers.Sublists[modifier], 1f);
					}
				}
				
			}
			else if (title.Rank == TitleRank.barony)
			{

				var holdingList = World.ProvinceEffects.Sublists["holding"];
				//holding type
				holdingList.Sublists.ForEach(title.Type, (sub) =>
				{
					CalcEffects(sub, multiplier);
				});
				var buildingList = holdingList.Sublists["buildings"];
				//holding buildings
				foreach (var building in title.Buildings)
				{
					if (buildingList.Sublists.ContainsKey(building.Type))
					{
						//high level buildings are already included in the list multiple times - no need to multiply by the level
						CalcEffects(buildingList.Sublists[building.Type], multiplier);// * building.Level);
					}
				}
			}
		}

		private void CalcEffects(PdxSublist effects, float multiplier)
		{
			baseTax += GetFloatEffect(effects, "base_tax", multiplier);
			baseProduction += GetFloatEffect(effects, "base_production", multiplier);
			baseManpower += GetFloatEffect(effects, "base_manpower", multiplier);
			fortLevel += GetFloatEffect(effects, "fort_level", multiplier);
			CentreOfTradeWeight += GetFloatEffect(effects, "centre_of_trade_weight", multiplier);
		}

		private float GetFloatEffect(PdxSublist effects, string key, float multiplier)
		{
			if (!effects.FloatValues.ContainsKey(key))
			{
				return 0;
			}
			return effects.FloatValues[key].Sum() * multiplier;
		}

		public override PdxSublist GetHistoryFile()
		{
			var data = new PdxSublist(null, FileName);
			foreach (var core in Cores)
			{
				if (core != null)
				{
					data.KeyValuePairs.Add("add_core", core.CountryTag);
				}
			}
			if (Owner != null)
			{
				data.KeyValuePairs["owner"] = Owner.CountryTag;
			}
			if (ControllerTag != null)
			{
				data.AddValue("controller", ControllerTag);
			}
			if (Culture != null)
			{
				data.KeyValuePairs["culture"] = Culture;
			}
			if (Religion != null)
			{
				data.KeyValuePairs["religion"] = Religion;
			}
			data.BoolValues["hre"] = new List<bool>();
			data.BoolValues["hre"].Add(IsHRE);

			data.AddValue("base_tax", BaseTax.ToString());
			data.AddValue("base_production", BaseProduction.ToString());
			data.AddValue("base_manpower", BaseManpower.ToString());
			//todo: trade goods

			data.AddValue("is_city", (Owner != null) ? "yes" : "no");

			if(LatentTradeGoods.Count != 0)
			{
				var latentGoods = new PdxSublist();
				LatentTradeGoods.ForEach((good) =>
				{
					latentGoods.AddValue(good);
				});

				data.AddSublist("latent_trade_goods", latentGoods);
			}

			if (TradeGood != null)
			{
				data.AddValue("trade_goods", TradeGood);
			}

            if(CentreOfTradeWeight >= 1)
            {
                data.AddValue("center_of_trade", ((int)Math.Min(3, CentreOfTradeWeight)).ToString());
            }

			foreach (var mod in Modifiers)
			{
				var modSub = new PdxSublist();
				modSub.AddValue("name", mod);
				modSub.AddValue("duration", "-1");

				data.AddSublist("add_permanent_province_modifier", modSub);
			}

			//TODO: something more sophisticated
			data.AddValue("discovered_by", "western");
			data.AddValue("discovered_by", "muslim");
			data.AddValue("discovered_by", "ottoman");
			data.AddValue("discovered_by", "eastern");
			data.AddValue("discovered_by", "indian");
			data.AddValue("discovered_by", "sub_saharan");

			if (Revolt != null && RevoltArmy != -1)
			{
				var revolt = new PdxSublist();
				revolt.AddValue("type", Revolt);
				revolt.AddValue("size", RevoltArmy.ToString());
				if (RevoltLeader != null)
				{
					revolt.AddValue("leader", RevoltLeader);
				}

				data.AddSublist("revolt", revolt);

				
			}
			return data;
		}
	}
}
