using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK2Helper;
using PdxFile;
using PdxUtil;
using System.IO;

namespace CK2ToEU4
{
    
	public class CK2Save : CK2World
	{
		public string FilePath { get; set; }

		public PdxSublist RootList { get; set; }
		public Eu4World Eu4World { get; internal set; }
		/// <summary>
		/// Use the CK2 date as EU4's start date?
		/// </summary>
		public bool KeepStartDate { get; set; }
		public Dictionary<int, CK2Revolt> CK2Revolts { get; set; }
		
		public string ChinaDisplayName { get; set; }
		public CK2Save(string filePath, string modPath, bool keepStartDate): base(modPath)
		{
            FilePath = filePath;
			KeepStartDate = keepStartDate;
			Console.WriteLine("Reading CK2 save file...");
			RootList = PdxSublist.ReadFile(filePath, "CK2txt");
			Date = RootList.KeyValuePairs["date"];
            Console.WriteLine(RootList.Sublists.ContainsKey("artifacts"));
			LoadDynasties();
			LoadCharacters();
            LoadArtifacts();
            LoadDynamicReligions();
			LoadTitles();
			LoadProvinces();
			LoadPostCharacters();
			LoadCultureCentres();
			//LoadWars();
			LoadChina();
			LoadVanillaTitleData();
            LoadReligiousData();


        }

        private void LoadArtifacts()
        {
            
        }

        private void LoadPostCharacters()
		{
			foreach (var character in CK2Characters)
			{
				character.Value.PostInitialise();
			}
		}

		private void LoadChina()
		{
			var offmapPowers = RootList.Sublists["offmap_powers"];
			var china = offmapPowers.Sublists.Single(pow => pow.Value.KeyValuePairs["type"] == "offmap_china").Value;
			ChinaDisplayName = Localisation[china.Sublists["names"].Values.Last()];
		}

		public void LoadWars()
		{
			CK2Revolts = new Dictionary<int, CK2Revolt>();

			RootList.Sublists.ForEach("active_war", war =>
			{
				var revolt = new CK2Revolt(war);
				if (Eu4World.RevoltMapper.ContainsKey(revolt.CasusBelli))
				{
					CK2Revolts.Add(revolt.Attacker, revolt);
				}
			});

		}

		private void LoadVanillaTitleData()
		{
			Console.WriteLine("Loading CK2 title data...");
			var titles = GetFilesFor(@"common\landed_titles");
			foreach(var title in titles)
			{
				if(Path.GetExtension(title) != ".txt")
				{
					continue;
				}
				var data = PdxSublist.ReadFile(title);
				RecursiveTitleSearch(data);
			}

		}

		private void RecursiveTitleSearch(PdxSublist data)
		{
			data.ForEachSublist((sub) =>
			{
				if (CK2Titles.ContainsKey(sub.Key) && CK2Titles[sub.Key].Colour == null && sub.Value.Sublists.ContainsKey("color"))
				{
					CK2Titles[sub.Key].Colour = new Colour(sub.Value.Sublists["color"].FloatValues[string.Empty]);
				}
				RecursiveTitleSearch(sub.Value);
			});
		}

		private void LoadDynasties()
		{
			//CK2Dynasties = new Dictionary<int, CK2Dynasty>();
			Console.WriteLine("Loading dynamic CK2 dynasties...");
			var dynasties = RootList.Sublists["dynasties"];
			dynasties.ForEachSublist((sub) =>
			{
				var id = int.Parse(sub.Key);
				if (!CK2Dynasties.ContainsKey(id))
				{
					CK2Dynasties[id] = new CK2Dynasty(this, sub.Value);
				}
			});
		}

		private void LoadCultureCentres()
		{
			foreach(var culture in CK2Cultures)
			{
				culture.Value.FindCentre(this);
				
			}
		}


		private void LoadTitles()
		{
			Console.WriteLine("Loading titles...");

			CK2Titles = new Dictionary<string, CK2Title>();
			CK2IndependentTitles = new Dictionary<string, CK2Title>();
			CK2TopLevelVassals = new Dictionary<string, CK2Title>();
			RootList.Sublists["title"].ForEachSublist(sub =>
            {
                // if it doesn't have "active = no"
                if (!sub.Value.BoolValues.ContainsKey("active") || sub.Value.BoolValues["active"].Single())
                {
                    CK2Titles.Add(sub.Key, new CK2Title(sub.Key, this, sub.Value));
                }
				
			});

			//dyn titles 
			RootList.Sublists.ForEach("dyn_title", sub =>
			{
				if (sub.KeyValuePairs.ContainsKey("base_title"))
				{
                    if (CK2Titles.ContainsKey(sub.KeyValuePairs["title"]))
                    {
                        CK2Titles[sub.KeyValuePairs["title"]].BaseTitle = CK2Titles[sub.KeyValuePairs["base_title"]];
                    }
				}
			});
		}

		private void LoadProvinces()
		{
			Console.WriteLine("Loading provinces...");
			//CK2Provinces = new List<CK2Province>();
			RootList.Sublists["provinces"].ForEachSublist(sub =>
			{
                var id = int.Parse(sub.Key);
                //CK2Provinces might not contain an entry if that entry is a wasteland
                bool prov1 = false;
                if (CK2Provinces.ContainsKey(id)) 
                {
                    prov1 = CK2Provinces[id].InitFromSaveFile(sub.Value);
                }
                
                if (CK2ProvinceDupes.ContainsKey(id))
                {
                    var prov2 = CK2ProvinceDupes[id].InitFromSaveFile(sub.Value);
                    if(!prov1 && prov2)
                    {
                        CK2Provinces[id] = CK2ProvinceDupes[id];
                    }
                }
			});

			//Console.WriteLine("Waiting for map search to complete...");
			//Task.WhenAll(TaskPool).Wait();

		}
        private void LoadDynamicReligions()
        {
            Console.WriteLine("Loading dynamic religions...");
            RootList.Sublists["religion"].ForEachSublist(sub =>
            {
                if (!CK2Religions.ContainsKey(sub.Key))
                {
                    CK2Religions[sub.Key] = new CK2Religion(sub.Key);
                }
            });
        }
        private void LoadReligiousData()
        {
            Console.WriteLine("Loading religious data...");
            RootList.Sublists["religion"].ForEachSublist(sub =>
            {
                CK2Religions[sub.Key].initFromSave(sub.Value, this);
            });
        }

		private void LoadCharacters()
		{
			Console.WriteLine("Loading characters...");
			CK2Characters = new Dictionary<int, CK2CharacterBase>();

			RootList.Sublists["character"].ForEachSublist(sub =>
			{
				CK2Character ch = new CK2Character(this, sub.Value);
				if (sub.Value.BoolValues.ContainsKey("player"))
				{
					ch.PrimaryTitleID = RootList.KeyValuePairs["player_realm"];
				}
				CK2Characters.Add(ch.ID, ch);
			});
		}
	}
}
