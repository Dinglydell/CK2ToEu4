using Microsoft.VisualBasic.FileIO;
using Eu4Helper;
using PdxFile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK2Helper;
using System.Text.RegularExpressions;

namespace CK2ToEU4
{
	public class Formable
	{
		public Eu4Country Country { get; set; }
		public CK2Title Title { get; set; }
		public List<Eu4Province> Provinces { get; set; }

		public Formable(CK2Title title, Eu4Country country)
		{

			Title = title;
			Country = country;
			Provinces = new List<Eu4Province>();
		}


		public PdxSublist GetDecision(Eu4World world)
		{
			if (Provinces.Count == 0)
			{
				return null;
			}
			if (Title.Name == "e_russia")
			{
				Console.WriteLine();
			}
			//group by areas
			var requiredProvs = Provinces.GroupBy(p => p.Area)
				.Select(g => g.OrderByDescending(p => p.Development).First()) //select top province in each area
				.OrderByDescending(p => p.Development) //order these provs
				.Take(Math.Max(3, Provinces.Count / 10)); //take the top 10%
			var decision = new PdxSublist(null, $"form_{Country.CountryTag}");
			decision.AddValue("major", "yes");
			var potential = new PdxSublist();
			var notFormed = new PdxSublist();
			notFormed.AddValue("has_country_flag", "formed_empire");
			if (Title.Rank == TitleRank.kingdom)
			{
				notFormed.AddValue("has_country_flag", "formed_kingdom");
			}
			potential.AddSublist("NOT", notFormed);
			var notHRE = new PdxSublist();
			notHRE.AddValue("tag", "HLR");
			potential.AddSublist("NOT", notHRE);

			var notExists = new PdxSublist();
			notExists.AddValue("exists", Country.CountryTag);
			potential.AddSublist("NOT", notExists);

			if (Title.Rank == TitleRank.kingdom)
			{ // kingdoms must be the right culture, empires the right culture group
				potential.AddValue("primary_culture", Provinces.GroupBy(p => p.Culture).OrderByDescending(pg => pg.Sum(p => p.Development)).First().First().Culture);
			}
			else
			{
				potential.AddValue("culture_group", world.Cultures[Provinces.GroupBy(p => world.Cultures[p.Culture].Group).OrderByDescending(pg => pg.Sum(p => p.Development)).First().First().Culture].Group.Name);
			}
			potential.AddValue("is_colonial_nation", "no");

			decision.AddSublist("potential", potential);

			//provinces_to_highlight
			var provincesToHighlight = new PdxSublist();
			var provOR = new PdxSublist();
			foreach (var prov in requiredProvs)
			{
				provOR.AddValue("province_id", prov.ProvinceID.ToString());
			}
			provincesToHighlight.AddSublist("OR", provOR);

			var ownerProvOR = new PdxSublist();
			var notOwned = new PdxSublist();
			notOwned.AddValue("owned_by", "ROOT");
			var notCored = new PdxSublist();
			notCored.AddValue("is_core", "ROOT");
			ownerProvOR.AddSublist("NOT", notOwned);
			ownerProvOR.AddSublist("NOT", notCored);

			provincesToHighlight.AddSublist("OR", ownerProvOR);

			decision.AddSublist("provinces_to_highlight", provincesToHighlight);


			//allow
			var allow = new PdxSublist();
			allow.AddValue("is_at_war", "no");
			allow.AddValue("is_free_or_tributary_trigger", "yes");
			allow.AddValue("is_nomad", "no");
			foreach (var prov in requiredProvs)
			{
				allow.AddValue("owns_core_province", prov.ProvinceID.ToString());
			}

			decision.AddSublist("allow", allow);


			//effect
			var effect = new PdxSublist();
			effect.AddValue("change_tag", Country.CountryTag);
			effect.AddValue("swap_non_generic_missions", "yes");
			effect.AddValue("remove_non_electors_emperors_from_empire_effect", "yes");
			var govRank = Title.Rank == TitleRank.kingdom ? 2 : 3;
			var ifGovRank = new PdxSublist();
			var limitGovRank = new PdxSublist();
			var limitGovRankNOT = new PdxSublist();
			limitGovRankNOT.AddValue("government_rank", govRank.ToString());
			limitGovRank.AddSublist("NOT", limitGovRankNOT);
			ifGovRank.AddSublist("limit", limitGovRank);
			ifGovRank.AddValue("set_government_rank", govRank.ToString());
			effect.AddSublist("if", ifGovRank);

			effect.AddValue("add_prestige", "25");

			foreach (var prov in Provinces)
			{
				var provEffect = new PdxSublist();
				var provEffectIf = new PdxSublist();
				var provEffectLimit = new PdxSublist();
				var provEffectLimitNOT = new PdxSublist();
				provEffectLimitNOT.AddValue("owned_by", "ROOT");
				provEffectLimit.AddSublist("NOT", provEffectLimitNOT);
				provEffectIf.AddSublist("limit", provEffectLimit);
				provEffectIf.AddValue("add_permanent_claim", Country.CountryTag);
				provEffect.AddSublist("if", provEffectIf);

				effect.AddSublist(prov.ProvinceID.ToString(), provEffect);
			}

			var centralisationModifier = new PdxSublist();
			centralisationModifier.AddValue("name", "centralization_modifier");
			centralisationModifier.AddValue("duration", "7300");
			effect.AddSublist("add_country_modifier", centralisationModifier);
			effect.AddValue("set_country_flag", $"formed_{Enum.GetName(typeof(TitleRank), Title.Rank)}");
			decision.AddSublist("effect", effect);

			var aiWillDo = new PdxSublist();
			aiWillDo.AddValue("factor", "1");

			decision.AddSublist("ai_will_do", aiWillDo);


			return decision;
		}

		public void AddLocalisation(Dictionary<string, string> locale)
		{
			locale.Add($"form_{Country.CountryTag}_title", $"Form {Country.DisplayNoun}");
			locale.Add($"form_{Country.CountryTag}_desc", $"");
		}
	}

	public class Eu4World : Eu4WorldBase
	{
		/// <summary>
		/// Prefix letter for dynamic country tag
		/// </summary>
		private readonly string[] LetterPrefixes = new string[] { "X", "Y", "Z", "A", "B", "D" };
		public Dictionary<int, ProvMap> ProvinceMapper { get; set; }

		//public Dictionary<string, string> GovernmentMapper { get; set; }
		//public Dictionary<string, string> TitleGovernmentMapper { get; private set; }

		public Dictionary<string, string> CountryMapper { get; set; }
		public Dictionary<string, string> CountryFileMapper { get; set; }

		public Dictionary<string, string> ReligionMapper { get; set; }
		public Dictionary<string, string> CultureMapper { get; set; }

		public Dictionary<string, string> RevoltMapper { get; set; }

		public Dictionary<Eu4SuperRegion, string> SuperRegionTechGroup { get; set; }

		public CK2Save CK2World { get; private set; }
		public int NumCustomCountries { get; set; }
		public List<NationalIdea> NationalIdeas { get; set; }
		public PdxSublist ProvinceEffects { get; set; }

		internal Eu4Culture MapCulture(CK2Culture culture)
		{
			var cul = CultureMapper[culture.Name];

			if (Cultures.ContainsKey(cul))
			{
				return Cultures[cul];
			}

			//TODO: splitter logic - also things such as anglo-saxon culture group
			if (CultureGroups.ContainsKey(cul))
			{
				return CultureGroups[cul].Cultures[0];
			}


			//create new
			CultureGroups[cul] = new Eu4CultureGroup(cul);
			Cultures[cul] = CultureGroups[cul].AddCulture(cul, this, false, culture.DisplayName);

			return Cultures[cul];
		}



		public PdxSublist CountryEffects { get; set; }
		public string TechiestRegion { get; private set; }
		public CK2Title HRE { get; internal set; }
		public HashSet<string> TakenTags { get; private set; }
		public string StartDate { get { return CK2World.KeepStartDate ? CK2World.Date : "1444.11.1"; } }

		public Dictionary<CK2Title, Formable> Formables { get; private set; }

		public Eu4World(CK2Save ck2World) : base("")
		{
			CK2World = ck2World;
			CK2World.Eu4World = this;

			Countries = new Dictionary<string, Eu4CountryBase>();
			TakenTags = new HashSet<string>();
			HRE = CK2World.CK2Titles["e_hre"];
			Console.WriteLine("Constructing EU4 world...");

			LoadWorldMap();
			LoadMappers();
			CK2World.LoadWars();
			LoadEffects();
			PostInitLoad();
			MapCk2Provinces();
			FindSuperRegionTech();
			//LoadFormables();
			PostInitCountries();
			SetupInstitutions();

			Formables = Formables.Where(f => f.Value.Provinces.Count >= 3).ToDictionary(f => f.Key, f => f.Value);
			var groups = Formables.GroupBy(f => f.Value.Country.CountryTag);
			if (groups.OrderByDescending(f => f.Count()).First().Count() > 1) // filter out duplicates (eg. k_france vs e_france)
			{
				Formables = groups.Select(g => g.OrderBy(h => h.Key.Rank).First()).ToDictionary(f => f.Key, f => f.Value);
			}

			WriteMod();
		}
		/// <summary>
		/// Load formable nations
		/// </summary>
		private void LoadFormables()
		{
			Formables = new Dictionary<CK2Title, Formable>();
			var formables = CK2World.CK2Titles.Where(t => t.Value.Holder == null && (t.Value.Rank == TitleRank.empire || t.Value.Rank == TitleRank.kingdom));
			foreach (var formable in formables)
			{
				var ctry = GetCountryFromTitle(formable.Value, true);
				Formables.Add(formable.Value, new Formable(formable.Value, ctry));
			}
		}

		private void FindSuperRegionTech()
		{
			var regionTech = new Dictionary<string, float>();
			foreach (var sr in SuperRegions)
			{
				//average tech per province in superregion
				regionTech[sr.Key] = sr.Value.Regions == null ? 0 : sr.Value.Regions.Sum(r => r.Areas == null ? 0 : r.Areas.Sum(a => a.Provinces.Sum(p => ((Eu4Province)Provinces[p]).TotalTech))) / sr.Value.Regions.Sum(r => r.Areas == null ? 0 : r.Areas.Sum(a => a.Provinces.Count));
			}
			var techiest = regionTech.Where(r => !r.Key.Contains("sea") && r.Value > 0).OrderByDescending(r => r.Value);
			//TODO: non-hardcoded thing here
			string[] techGroups = new string[] { "western", "eastern", "ottoman", "muslim", "indian", "sub_saharan", "central_african" };
			var n = 0;
			SuperRegionTechGroup = new Dictionary<Eu4SuperRegion, string>();
			foreach (var sr in techiest)
			{
				SuperRegionTechGroup[SuperRegions[sr.Key]] = techGroups[Math.Min(n, techGroups.Length - 1)];
				n += techiest.Count() / techGroups.Length;
			}
			Console.WriteLine($"The techiest superregion is {techiest.First().Key}");
		}

		private void PostInitCountries()
		{
			if (!Countries.Any(c => c.Value.CountryTag == "MNG"))
			{ //if no china, make china
				var china = new Eu4Country(this, null, "MNG", "MNG - Ming", true, CK2World.CK2Titles["e_china_west_governor"]);
				china.DisplayNoun = CK2World.ChinaDisplayName;
				china.DisplayAdj = CK2World.ChinaDisplayName;
				//china.Government
				Countries.Add("china", china);
			}

			//HRE electors
			var electors = Countries.Where(c => c.Value.InHRE).OrderByDescending(c => c.Value.Development).Take(7);
			foreach (var el in electors)
			{
				el.Value.IsElector = true;
			}


			foreach (var country in Countries)
			{
				((Eu4Country)country.Value).PostInitialise();
			}
		}

		private void WriteMod()
		{
			DeleteDirectoryRecursive("output");

			Directory.CreateDirectory("output");
			Console.WriteLine("Copying mod template...");
			CopyDirectory(@"template\copy", "output");
			Console.WriteLine("Writing EU4 mod files...");
			Directory.CreateDirectory(@"output\history");
			Directory.CreateDirectory(@"output\history\provinces");
			Console.WriteLine("Writing province history...");
			foreach (var prov in Provinces)
			{
				var history = prov.Value.GetHistoryFile();
				if (prov.Value.FileName != null)
				{
					using (var file = new StreamWriter($@"output\history\provinces\{prov.Value.FileName}.txt"))
					{
						history.WriteToFile(file);
					}
				}

			}
			Directory.CreateDirectory(@"output\common");
			Directory.CreateDirectory(@"output\common\countries");
			Directory.CreateDirectory(@"output\common\country_tags");
			Directory.CreateDirectory(@"output\history\countries");

			Directory.CreateDirectory(@"output\gfx");
			Directory.CreateDirectory(@"output\gfx\flags");

			Directory.CreateDirectory(@"output\common\ideas");
			Directory.CreateDirectory(@"output\history\diplomacy");

			Console.WriteLine("Writing country files...");
			var tags = new PdxSublist(null, "converted_country_tags.txt");
			var ideas = new PdxSublist(null, "converted_country_ideas.txt");
			var diplomacy = new PdxSublist(null, "converted_diplomacy.txt");
			foreach (var form in Formables)
			{
				Countries.Add(form.Key.Name, form.Value.Country);
			}
			foreach (var country in Countries)
			{
				if (!country.Value.IsVanilla)
				{
					var countryData = country.Value.GetCountryFile();
					tags.AddValue(country.Value.CountryTag, "countries/" + countryData.Key);


					using (var file = new StreamWriter($@"output\common\countries\{countryData.Key}"))
					{
						countryData.WriteToFile(file);
					}

					//country flag
					var flag = ((Eu4Country)country.Value).GetFlagPath();
					if (flag != null)
					{
						File.Copy(flag, $@"output\gfx\flags\{country.Value.CountryTag}.tga");
					}


				}
				var historyData = country.Value.GetHistoryFile();

				using (var file = new StreamWriter($@"output\history\countries\{historyData.Key}"))
				{
					historyData.WriteToFile(file);
				}

				var ideaData = ((Eu4Country)country.Value).GetNationalIdeas();

				ideas.AddSublist($"{country.Value.CountryTag}_ideas", ideaData);

				country.Value.AddDiplomacy(diplomacy);
				//todo: national ideas
			}
			//put country tags in file
			using (var file = new StreamWriter($@"output\common\country_tags\{tags.Key}"))
			{
				tags.WriteToFile(file);
			}
			//put national ideas in file
			using (var file = new StreamWriter($@"output\common\ideas\{ideas.Key}"))
			{
				ideas.WriteToFile(file);
			}
			//put diplomacy in file
			using (var file = new StreamWriter($@"output\history\diplomacy\{diplomacy.Key}"))
			{
				diplomacy.WriteToFile(file);
			}

			//culture
			Directory.CreateDirectory(@"output\common\cultures");
			var cultureData = new PdxSublist();

			foreach (var cul in CultureGroups)
			{
				if (cul.Value.AnyNew)
				{
					cultureData.AddSublist(cul.Key, cul.Value.GetGroupData());
				}
			}
			using (var file = new StreamWriter(@"output\common\cultures\converted_cultures.txt"))
			{
				cultureData.WriteToFile(file);
			}


			//institutions

			var instFile = File.ReadAllText(@"template\institutions.txt").Replace("%HIGHEST_TECH_REGION%", TechiestRegion);
			Directory.CreateDirectory(@"output\common\institutions");
			File.WriteAllText(@"output\common\institutions\00_Core.txt", instFile);



			//localisation
			var locale = new Dictionary<string, string>();

			foreach (var country in Countries)
			{
				((Eu4Country)country.Value).AddLocalisation(locale);
			}
			//china
			//locale.Add("MNG", CK2World.ChinaDisplayName);
			//locale.Add("MNG_ADJ", CK2World.ChinaDisplayName);

			foreach (var cg in CultureGroups)
			{
				cg.Value.AddLocalisation(locale);

			}
			foreach (var idea in NationalIdeas)
			{
				idea.AddLocalisation(locale);
			}
			foreach (var form in Formables)
			{
				form.Value.AddLocalisation(locale);
			}

			Directory.CreateDirectory(@"output\localisation");
			using (var file = new StreamWriter(@"output\localisation\zz_converted_l_english.yml", false, new UTF8Encoding(true)))
			{
				file.WriteLine("l_english:");
				foreach (var l in locale)
				{
					file.Write(" ");
					file.WriteLine($"{l.Key}:0 \"{l.Value}\"");
				}
			}

			//decisions
			Directory.CreateDirectory(@"output\decisions");
			var formableDecisionsFile = new PdxSublist(null, @"output\decisions\converted_formable_nations.txt");
			var formableDecisions = new PdxSublist();
			foreach (var formable in Formables)
			{
				var dec = formable.Value.GetDecision(this);
				if (dec != null)
				{
					formableDecisions.AddSublist(dec.Key, dec);
				}
			}
			formableDecisionsFile.AddSublist("country_decisions", formableDecisions);
			using (var file = new StreamWriter(formableDecisionsFile.Key))
			{
				formableDecisionsFile.WriteToFile(file);
			}

			WriteDefines();

			Directory.CreateDirectory(@"output\common\technologies");
			FixTech();



		}

		private void FixTech()
		{
			if (CK2World.KeepStartDate)
			{
				var admTech = PdxSublist.ReadFile(@"output\common\technologies\adm.txt");
				var dipTech = PdxSublist.ReadFile(@"output\common\technologies\dip.txt");
				var milTech = PdxSublist.ReadFile(@"output\common\technologies\mil.txt");

				var startYear = int.Parse(StartDate.Substring(0, StartDate.IndexOf('.')));
				FixTech(admTech, startYear);
				FixTech(dipTech, startYear);
				FixTech(milTech, startYear);

				using (var admFile = new StreamWriter(@"output\common\technologies\adm.txt"))
				{
					admTech.WriteToFile(admFile);
				}
				using (var dipFile = new StreamWriter(@"output\common\technologies\dip.txt"))
				{
					dipTech.WriteToFile(dipFile);
				}
				using (var milFile = new StreamWriter(@"output\common\technologies\mil.txt"))
				{
					milTech.WriteToFile(milFile);
				}
			}
		}

		private void FixTech(PdxSublist techFile, int startYear)
		{
			if (techFile.Sublists.ContainsKey("ahead_of_time"))
			{
				techFile.Sublists.Remove("ahead_of_time");
			}
			techFile.Sublists.ForEach("technology", (tech) =>
			{
				var year = tech.FloatValues["year"].Single();
				tech.FloatValues["year"].Clear();
				tech.FloatValues["year"].Add(year - 1444 + startYear);
			});
		}

		private void WriteDefines()
		{
			if (CK2World.KeepStartDate)
			{
				Directory.CreateDirectory(@"output\common\defines");

				using (var file = new StreamWriter(@"output\common\defines\converted_defines.lua"))
				{
					file.WriteLine($"NDefines.NGame.START_DATE = \"{CK2World.Date}\"");
					file.WriteLine($"NDefines.NGame.END_DATE = \"1836.1.1\"");
				}
			}
		}

		private void DeleteDirectoryRecursive(string path)
		{
			if (Directory.Exists(path))
			{
				foreach (var subDir in Directory.GetDirectories(path))
				{
					DeleteDirectoryRecursive(subDir);
				}
				Directory.Delete(path, true);
			}
		}

		private void CopyDirectory(string sourceDir, string destDir)
		{
			if (!Directory.Exists(destDir))
			{
				Directory.CreateDirectory(destDir);
			}
			var dir = new DirectoryInfo(sourceDir);
			var files = dir.GetFiles();
			foreach (var file in files)
			{
				file.CopyTo(Path.Combine(destDir, file.Name));
			}

			foreach (var subDir in dir.GetDirectories())
			{
				CopyDirectory(subDir.FullName, Path.Combine(destDir, subDir.Name));
			}
		}

		private void SetupInstitutions()
		{
			var regionTech = new Dictionary<string, float>();
			foreach (var region in Regions)
			{
				regionTech[region.Key] = region.Value.Areas == null ? 0 : region.Value.Areas.Sum(a => a.Provinces.Sum(p => ((Eu4Province)Provinces[p]).TotalTech)) / region.Value.Areas.Sum(a => a.Provinces.Count);
			}
			var techiest = regionTech.OrderByDescending(r => r.Value);

			TechiestRegion = techiest.First().Key;
			Console.WriteLine("The most technologically advanced region is " + TechiestRegion);



			//TODO: make renaissance restricted to techiest region
		}


		public List<Eu4Country> GetCountriesFromCharacter(CK2CharacterBase holder)
		{
			if (holder == null)
			{
				return null;
			}

			var countries = new List<Eu4Country>();
			//todo: make HRE less hardcoded pls
			if (holder.Liege != null && holder.Liege.PrimaryTitle != HRE)
			{
				countries.AddRange(GetCountriesFromCharacter(holder.Liege));
				if (countries.Count != 1) //if there's one country, we must be a direct vassal so continue
				{
					return countries;
				}
			}


			if (!holder.PrimaryTitle.IsRevolt)
			{
				countries.Add(GetCountryFrom(holder));
			}
			return countries;
		}

		public Eu4Country GetIndependentCountryFromCharacter(CK2Character holder)
		{
			if (holder == null)
			{
				return null;
			}
			if (holder.ID == 1274212)
			{
				Console.WriteLine();
			}
			if (holder.Liege != null && holder.Liege.PrimaryTitle != HRE)
			{
				//if liege has no liege, check if we should be a vassal
				if ((holder.Liege.Liege == null) || holder.Liege.Liege.PrimaryTitle == HRE)
				{
					//work out if we should be a vassal, or absorbed (existing as a country in eu4)
					var liegeCountry = GetCountriesFromCharacter(holder.Liege).Single();
					var vassalThreshhold = liegeCountry.VassalThreshhold;
					var strength = holder.GetTotalStrength();
					var liegeStrength = ((CK2Character)holder.Liege).GetTotalStrength() - strength;
					if (liegeStrength < 0)
					{
						Console.WriteLine();
					}
					if (!holder.PrimaryTitle.IsRevolt && liegeStrength * vassalThreshhold < strength)
					{
						//we're stronk! become a vassal
						var country = GetCountryFrom(holder);

						Console.WriteLine($"We, {holder.PrimaryTitle.DisplayName}, are stronk. ({strength}/{liegeStrength} - {Math.Round(((float)strength) / liegeStrength * 100)}%/{Math.Round(vassalThreshhold * 100)}%)");
						country.MakeVassalOf(GetCountryFrom(holder.Liege));
						return country;
					}

				}
				return GetIndependentCountryFromCharacter((CK2Character)holder.Liege);


			}

			return GetCountryFrom(holder);
		}

		private Eu4Country GetCountryFrom(CK2CharacterBase holder)
		{
			if (!Countries.ContainsKey(holder.ID.ToString()))
			{

				var primTitle = holder.PrimaryTitle;
				if (primTitle == HRE)
				{
					primTitle = holder.Titles.Where(t => t != HRE).OrderByDescending(t => t.Rank).First();
				}


				Countries[holder.ID.ToString()] = GetCountryFromTitle(primTitle);
				if (holder.Liege?.PrimaryTitle?.Name == "e_hre")
				{
					Countries[holder.ID.ToString()].InHRE = true;
				}
			}
			return (Eu4Country)Countries[holder.ID.ToString()];
		}

		private Eu4Country GetCountryFromTitle(CK2Title primTitle, bool forceDuplicate = false)
		{

			bool vanilla = true;
			string fileName = null;
			string tag = null;
			if (CountryMapper.ContainsKey(primTitle.Name))
			{
				tag = CountryMapper[primTitle.Name];
				fileName = CountryFileMapper[primTitle.Name];
			}
			if (tag == null || (!forceDuplicate && TakenTags.Contains(tag)))
			{

				vanilla = false;

				tag = LetterPrefixes[NumCustomCountries / 100] + (NumCustomCountries % 100).ToString("D2");
				NumCustomCountries++;
				fileName = tag;
				CountryMapper[primTitle.Name] = tag;
				CountryFileMapper[primTitle.Name] = fileName;
			}
			TakenTags.Add(tag);
			return new Eu4Country(this, primTitle.Holder, tag, fileName, vanilla, primTitle);
		}

		internal Eu4Culture MapCulture(CK2Province province)
		{
			var cul = CultureMapper[province.Culture.Name];

			var distance = province.Culture.GetDistanceTo(province);
			//TODO: no magic threshhold number
			var threshhold = 10000;
			var culture = province.Culture;
			if (distance > threshhold)
			{
				CK2Culture closest = null;
				var closeDist = int.MaxValue;
				foreach (var sub in province.Culture.SubCultures)
				{
					var dist = sub.GetDistanceTo(province);
					if (dist < closeDist && dist < threshhold)
					{
						closeDist = dist;
						closest = sub;
					}
				}
				if (closest == null)
				{
					var sub = province.Culture.CreateSubCulture(province);
					culture = sub;
				}
				else
				{
					cul = closest.Name;
					culture = closest;
				}
			}

			if (Cultures.ContainsKey(cul))
			{
				return Cultures[cul];
			}

			//TODO: splitter logic - also things such as anglo-saxon culture group
			if (CultureGroups.ContainsKey(cul))
			{
				return CultureGroups[cul].Cultures[0];
			}


			//create new
			Eu4CultureGroup group;
			if (culture.IsSubCulture)
			{
				var parent = CultureMapper[culture.Parent.Name];
				if (Cultures.ContainsKey(parent))
				{
					group = Cultures[parent].Group;
				}
				else if (CultureGroups.ContainsKey(parent))
				{
					group = CultureGroups[parent];

				}
				else
				{
					group = new Eu4CultureGroup(parent);
					CultureGroups[parent] = group;
				}

			}
			else
			{
				CultureGroups[cul] = new Eu4CultureGroup(cul);
				group = CultureGroups[cul];
			}
			Cultures[cul] = group.AddCulture(cul, this, false, culture.DisplayName);

			if (culture.IsSubCulture)
			{
				var parent = CultureMapper[culture.Parent.Name];
				if (Cultures.ContainsKey(parent))
				{
					if (string.IsNullOrEmpty(culture.Centre.CountyTitle.DisplayAdj ?? culture.Centre.CountyTitle.DisplayName) || string.IsNullOrEmpty(Cultures[parent].DisplayName))
					{
						Console.WriteLine();
					}
					Cultures[cul].DisplayName = culture.Centre.CountyTitle.DisplayAdj ?? culture.Centre.CountyTitle.DisplayName + "-" + Cultures[parent].DisplayName;
				}
				else
				{
					Cultures[cul].DisplayName = culture.Centre.CountyTitle.DisplayAdj;
				}
			}

			return Cultures[cul];
		}

		//internal Eu4Culture MapCulture(CK2Culture culture)
		//{
		//	var cul = CultureMapper[culture.Name];
		//	if (Cultures.ContainsKey(cul))
		//	{
		//		return Cultures[cul];
		//	}

		//	//TODO: splitter logic - also things such as anglo-saxon culture group
		//	if (CultureGroups.ContainsKey(cul))
		//	{
		//		return CultureGroups[cul].Cultures[0];
		//	}


		//	//create new
		//	CultureGroups[cul] = new Eu4CultureGroup(cul);
		//	Cultures[cul] = CultureGroups[cul].AddCulture(cul, this);

		//	return Cultures[cul];
		//}

		public Eu4Religion MapReligion(CK2Religion religion)
		{
			if (religion == null)
			{
				return null;
			}
			var rel = ReligionMapper[religion.Name];
			if (Religions.ContainsKey(rel))
			{
				return GetReligion(rel);
			}
			if (ReligiousGroups.ContainsKey(rel))
			{
				return ReligiousGroups[rel].Religions[0];
			}

			ReligiousGroups[rel] = new Eu4ReligionGroup(rel, this, rel);
			Religions[rel] = ReligiousGroups[rel].AddReligion(rel, this);

			return Religions[rel];

		}

		private void LoadEffects()
		{
			NationalIdeas = new List<NationalIdea>();
			var nis = PdxSublist.ReadFile("nationalIdeas.txt");

			nis.ForEachSublist(sub =>
			{
				NationalIdeas.Add(new NationalIdea(sub.Value));
			});

			ProvinceEffects = PdxSublist.ReadFile("provinceEffects.txt");
			CountryEffects = PdxSublist.ReadFile("countryEffects.txt");
		}

		private void LoadMappers()
		{
			ProvinceMapper = new Dictionary<int, ProvMap>();
			var historyFiles = GetFilesFor(@"history\provinces");
			var historyNames = new Dictionary<string, string>();
			foreach (var file in historyFiles)
			{
				var name = Path.GetFileNameWithoutExtension(file);
				var regex = new Regex(@"^\d+");

				historyNames[regex.Match(name).Value] = name;
			}
			LoadCsv("province_table.csv", (fields) =>
			{
				if (CK2World.CK2Titles.ContainsKey(fields[0]))
				{
					int eu4ProvID = int.Parse(fields[1]);
					//the filenames are bollocks for half the provinces
					//Provinces[eu4ProvID].FileName = fields[2];
					Provinces[eu4ProvID].FileName = historyNames[eu4ProvID.ToString()];
					if (!ProvinceMapper.ContainsKey(eu4ProvID))
					{
						ProvinceMapper[eu4ProvID] = new ProvMap();
					}
					//if(!ProvinceMapper[eu4ProvID].ck2Titles.Contains()
					ProvinceMapper[eu4ProvID].ck2Titles.Add(CK2World.CK2Titles[fields[0]]);
				}
			});



			//GovernmentMapper = new Dictionary<string, string>();
			//TitleGovernmentMapper = new Dictionary<string, string>();
			//LoadCsv("government_table.csv", (fields) =>
			//{
			//
			//	if (fields[0].StartsWith("gov_"))
			//	{
			//		var gov = fields[0].Substring(4);
			//		GovernmentMapper.Add(gov, fields[1]);
			//	}
			//	else
			//	{
			//		TitleGovernmentMapper.Add(fields[0], fields[1]);
			//	}
			//});


			CountryMapper = new Dictionary<string, string>();
			CountryFileMapper = new Dictionary<string, string>();

			LoadCsv("nation_table.csv", (fields) =>
			{
				if (fields[0] != string.Empty)
				{
					CountryMapper.Add(fields[0], fields[1]);
					CountryFileMapper.Add(fields[0], fields[2]);
				}
			});


			ReligionMapper = new Dictionary<string, string>();
			LoadCsv("religion_table.csv", (fields) =>
			{
				ReligionMapper.Add(fields[0], fields[1]);
			});
			CultureMapper = new Dictionary<string, string>();
			LoadCsv("culture_table.csv", (fields) =>
			{
				CultureMapper.Add(fields[0], fields[1]);
			});


			RevoltMapper = new Dictionary<string, string>();
			var mapper = PdxSublist.ReadFile("revolt_table.txt");
			mapper.ForEachString(kvp =>
			{
				RevoltMapper.Add(kvp.Key, kvp.Value);
			});
		}

		private void LoadCsv(string file, Action<string[]> callback)
		{
			using (TextFieldParser parser = new TextFieldParser(file))
			{
				parser.TextFieldType = FieldType.Delimited;
				parser.SetDelimiters(";");
				parser.ReadFields();
				while (!parser.EndOfData)
				{

					//Process row
					string[] fields = parser.ReadFields();

					//remove comments
					bool empty = true;
					bool foundHash = false;
					for (var i = 0; i < fields.Length; i++)
					{
						if (foundHash)
						{
							fields[i] = null;
						}
						else if (fields[i].Contains('#'))
						{
							fields[i] = fields[i].Substring(0, fields[i].IndexOf('#'));
							foundHash = true;
						}
						if ((fields[i]?.Length ?? 0) != 0)
						{
							empty = false;
						}
					}

					if (!empty)
					{
						fields = fields.Where(f => f != null).ToArray();
						callback(fields);
					}
				}
			}
		}

		private void MapCk2Provinces()
		{
			foreach (var prov in Provinces.Values) //init
			{
				if (ProvinceMapper.ContainsKey(prov.ProvinceID))
				{
					var map = ProvinceMapper[prov.ProvinceID];
					((Eu4Province)prov).Initialise(map.ck2Titles);
				}
			}
			LoadFormables();
			foreach (var prov in Provinces.Values) //post-init
			{
				if (ProvinceMapper.ContainsKey(prov.ProvinceID))
				{
					var map = ProvinceMapper[prov.ProvinceID];
					((Eu4Province)prov).PostInitialise(map.ck2Titles);
				}
			}
		}

		private void LoadWorldMap()
		{

			var files = GetFilesFor("map");


			Console.WriteLine("Loading EU4 areas..");

			var areaFile = files.Find(f => Path.GetFileName(f) == "area.txt");
			var areas = PdxSublist.ReadFile(areaFile);
			Areas = new Dictionary<string, Eu4Area>();
			Provinces = new Dictionary<int, Eu4ProvinceBase>();
			var provFiles = GetFilesFor(@"history\provinces");

			foreach (var provFile in provFiles)
			{
				var data = PdxSublist.ReadFile(provFile);
				var provID = int.Parse(new Regex("[0-9]+").Match(Path.GetFileName(provFile)).Value);
				Provinces.Add((int)provID, new Eu4Province(this, (int)provID, data));
			}
			foreach (var ar in areas.Sublists)
			{
				//Areas[ar.Key] = new HashSet<int>(ar.Value.FloatValues.Values.SelectMany(f => f.Select(e => (int)e)));
				if (ar.Value.FloatValues.Count != 0)
				{
					Areas[ar.Key] = new Eu4Area(ar.Key, ar.Value);
					foreach(var prov in Areas[ar.Key].Provinces)
					{
						Provinces[prov].Area = Areas[ar.Key];
					}
				}
			}

			

			Console.WriteLine("Loading EU4 regions...");
			var regionFile = files.Find(f => Path.GetFileName(f) == "region.txt");
			var regions = PdxSublist.ReadFile(regionFile);
			Regions = new Dictionary<string, Eu4Region>();
			foreach (var reg in regions.Sublists)
			{
				Regions[reg.Key] = new Eu4Region(reg.Key, reg.Value, this);
			}

			Console.WriteLine("Loading EU4 areas..");

			var continentFile = files.Find(f => Path.GetFileName(f) == "continent.txt");
			var continents = PdxSublist.ReadFile(continentFile);
			Continents = new Dictionary<string, Eu4Continent>();
			foreach (var con in continents.Sublists)
			{
				//Areas[ar.Key] = new HashSet<int>(ar.Value.FloatValues.Values.SelectMany(f => f.Select(e => (int)e)));
				Continents[con.Key] = new Eu4Continent(con.Key, con.Value);
			}


		}
	}
}
