using Eu4Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK2Helper;
using PdxFile;
using PdxUtil;

namespace CK2ToEU4
{
    public class NationalIdeaGroup
    {
        public string Name { get; set; }
        public List<NationalIdea> Ideas { get; set; }

        public NationalIdeaGroup(PdxSublist data)
        {
            Name = data.Key;
            Ideas = new List<NationalIdea>();
            data.ForEachSublist(sub =>
            {
                Ideas.Add(new NationalIdea(sub.Value, this));
            });
        }

        internal void AddLocalisation(Dictionary<string, string> locale)
        {
            foreach (var idea in Ideas)
            {
                idea.AddLocalisation(locale);
            }
        }
    }
	public class NationalIdea
	{
		public string Name { get; set; }
		public string DisplayTitle { get; set; }
		public string DisplayDesc { get; set; }
		public List<PdxSublist> Effects { get; set; }
        public NationalIdeaGroup Group { get; private set; }

        public NationalIdea(PdxSublist data, NationalIdeaGroup group)
		{
			Name = data.Key;
			DisplayTitle = data.KeyValuePairs["title"];
			DisplayDesc = data.KeyValuePairs["desc"];
			Effects = new List<PdxSublist>();
            Group = group;
			for (var i = 0; data.Sublists.ContainsKey($"level_{i}"); i++)
			{
				Effects.Add(data.Sublists[$"level_{i}"]);
			}
		}

		public void AddLocalisation(Dictionary<string, string> localisation)
		{
			for (var i = 0; i < Effects.Count; i++)
			{
				localisation.Add($"{Name}_{i}", DisplayTitle);
				localisation.Add($"{Name}_{i}_desc", DisplayDesc);
			}
		}
	}

    class NationalIdeaGroupScore {
        public NationalIdeaGroup Group { get; set; }
        public float Score { get; set; } 

        public NationalIdeaGroupScore(NationalIdeaGroup group, float score)
        {
            Group = group;
            Score = score;
        }
    }


	public class Eu4Country : Eu4CountryBase
	{
		protected CK2CharacterBase holder;
		private CK2Title title;
		public string FileName { get; set; }

		public new Eu4World World { get; set; }

		public Dictionary<NationalIdea, float> IdeaWeights { get; set; }
		public Dictionary<NationalIdea, float> IdeaLevels { get; set; }

		public int NumHoldings { get; set; }
		public float NumProvinces { get; set; }
		//weights from a holding
		private Dictionary<NationalIdea, float> IdeaWeightsHolding { get; set; }
		private Dictionary<NationalIdea, float> IdeaLevelsHolding { get; set; }

		private Dictionary<NationalIdea, float> IdeaWeightsProvince { get; set; }
		private Dictionary<NationalIdea, float> IdeaLevelsProvince { get; set; }

		private Dictionary<string, float> GovernmentTypeWeights { get; set; }
        private Dictionary<string, float> GovernmentReformWeights { get; set; }

        public List<NationalIdea> NationalIdeas { get; set; }
		public float VassalThreshhold { get; private set; }
		public string TechGroup { get; private set; }
        public float RenaissanceFactor { get; private set; }

        public Eu4Country(Eu4World world, CK2CharacterBase holder, string tag, string file, bool vanilla, CK2Title title = null) : base(world)
		{
			title = title ?? holder.PrimaryTitle;
			this.title = title;
			DisplayNoun = title.DisplayName;

			DisplayAdj = title.DisplayAdj;

			IsVanilla = vanilla;
			CountryTag = tag;
			World = world;
			FileName = file;
			Subjects = new List<string>();
			this.holder = holder;

			GovernmentRank = (byte)((title.Rank == TitleRank.kingdom) ? 2 : (title.Rank == TitleRank.empire ? 3 : 1));
            //if (world.TitleGovernmentMapper.ContainsKey(title.Name))
            //{
            //	Government = world.TitleGovernmentMapper[title.Name];
            //}
            //else
            //{
            //	//TODO: proper government flavour stuff
            ////world.GovernmentMapper
            //	//Government = "feudal_monarchy"; //world.GovernmentMapper[holder.GovernmentType];
            //}
            if (holder != null)
            {
                var prov = holder.Titles.Where(t => t.Rank == TitleRank.county).Select(t => t.Province).FirstOrDefault();
                PrimaryCulture = holder.Culture == prov?.Culture ? World.MapCulture(prov).Name : World.MapCulture(holder.Culture).Name;
                Religion = World.MapReligion(holder.Religion)?.Name;
            }
            AcceptedCultures = new List<string>();
            CalcEffects();
			Government = GovernmentTypeWeights.OrderByDescending(w => w.Value).First().Key;
            Reforms = new List<string>();
            Reforms.Add(GovernmentReformWeights.OrderByDescending(w => w.Value).First().Key);


		}

		public void PostInitialise()
		{
            

            if ((holder?.DynastyID ?? 0) != 0 && holder.Culture.DynastyTitleNames && !World.Countries.Any(c => ((Eu4Country)c.Value).holder.DynastyID == holder.DynastyID))
			{
				DisplayNoun = World.CK2World.CK2Dynasties[holder.DynastyID].Name;
				DisplayAdj = DisplayNoun;
			}
			
			var myProvs = World.Provinces.Where(p => p.Value.Owner == this);
			// of provinces where their respective ck2 titles contain my capital, order by development and pick highest
			Capital = myProvs.Where(p => World.ProvinceMapper[p.Key].ck2Titles.Contains(holder.Capital.LiegeTitle)).OrderByDescending(p => p.Value.Development).FirstOrDefault().Key;
			if(Capital == 0)
			{
				// if fail to find province, pick top one
				Capital = myProvs.OrderByDescending(p => p.Value.Development).FirstOrDefault().Key;
                //if that still fails, pick top core
                if(Capital == 0)
                {
                    Capital = World.Provinces.Where(p => p.Value.Cores.Contains(this)).OrderByDescending(p => p.Value.Development).FirstOrDefault().Key;
                }
			}
			if (Capital != 0)
			{
				var mySuperRegion = World.SuperRegions.Where(sr => !sr.Key.Contains("sea") && sr.Value.Regions.Any(r => r.Areas.Any(a => a.Provinces.Contains(Capital)))).Single();
				TechGroup = World.SuperRegionTechGroup[mySuperRegion.Value];
			}

            var myRebs = myProvs.Where(p => ((Eu4Province)p.Value).Revolt != null).GroupBy(p => ((Eu4Province)p.Value).Revolt);
			foreach (var reb in myRebs)
			{
				//	if(reb.Where(p => ((Eu4Province)p.Value).RevoltArmy).Count() == 0)
				//{
				//pick the highest level province to spawn a rebellion army
				var topProv = ((Eu4Province)reb.OrderBy(p => p.Value.Development).First().Value);
				topProv.RevoltArmy = 1;
				Console.WriteLine($"{CountryTag} {reb.Key}: {topProv.ProvinceID}");
				//	}
			}
		}


		/// <summary>
		/// Returns true if in CK2 this title would be a dejure part of this country
		/// </summary>
		/// <param name="t"></param>
		/// <returns></returns>
		internal bool IsDejure(CK2Title t)
		{
			return t.IsDirectDejureLiege(title);
		}

		private void CalcEffects()
		{
			IdeaWeights = new Dictionary<NationalIdea, float>();
			IdeaLevels = new Dictionary<NationalIdea, float>();

			IdeaWeightsHolding = new Dictionary<NationalIdea, float>();
			IdeaLevelsHolding = new Dictionary<NationalIdea, float>();

			IdeaWeightsProvince = new Dictionary<NationalIdea, float>();
			IdeaLevelsProvince = new Dictionary<NationalIdea, float>();
            RenaissanceFactor = 0;
			VassalThreshhold = 0;
			GovernmentTypeWeights = new Dictionary<string, float>();
            GovernmentReformWeights = new Dictionary<string, float>();
			var world = (Eu4World)World;
			var baseEffects = world.CountryEffects.Sublists["base"];
			CalcEffects(baseEffects);


			//type 
			
			//Console.WriteLine(title.Holder.GovernmentType);
			if (title.Holder != null)
			{
                var type = world.CountryEffects.Sublists["type"];
                if (type.Sublists.ContainsKey(title.Holder.GovernmentType))
				{
					CalcEffects(type.Sublists[title.Holder.GovernmentType]);
				}
				else
				{
					Console.WriteLine(title.Holder.GovernmentType);
				}
			}

			//laws
			var lawEffects = world.CountryEffects.Sublists["laws"];
			foreach (var law in title.Laws)
			{
				if (lawEffects.Sublists.ContainsKey(law))
				{
					CalcEffects(lawEffects.Sublists[law]);
				}
			}
            
            //religion
            if((title.Holder?.Religion?.Features?.Count ?? 0) != 0)
            {
                var religionEffects = world.CountryEffects.Sublists["religion"];
                foreach (var feature in title.Holder.Religion.Features)
                {
                    if (religionEffects.Sublists.ContainsKey(feature))
                    {
                        CalcEffects(religionEffects.Sublists[feature]);
                    }
                }
            }

            //artifacts
            if((title.Holder?.Artifacts.Count ?? 0) != 0)
            {
                var artifactEffects = world.CountryEffects.Sublists["artifacts"];

                foreach (var artifact in title.Holder.Artifacts)
                {
                    if (artifactEffects.Sublists.ContainsKey(artifact.Type))
                    {
                        CalcEffects(artifactEffects.Sublists[artifact.Type]);
                    }
                }
            }
		}

		public void CalcEffects(PdxSublist effects)
		{
			CalcEffects(effects, 1);
		}

        public void CalcEffects(PdxSublist effects, float multiplier)
        {
            CalcEffects(effects, IdeaWeights, IdeaLevels, multiplier);
        }

        private void CalcEffects(PdxSublist effects, Dictionary<NationalIdea, float> weight, Dictionary<NationalIdea, float> level)
        {
            CalcEffects(effects, weight, level, 1);

        }
            private void CalcEffects(PdxSublist effects, Dictionary<NationalIdea, float> weight, Dictionary<NationalIdea, float> level, float multiplier)
		{
			//ideas
			foreach (var ideaGroup in World.NationalIdeaGroups)
			{
                var groupWeight = GetFloatEffect(effects, $"idea_{ideaGroup.Name}_weight") * multiplier;
                var groupLevel = GetFloatEffect(effects, $"idea_{ideaGroup.Name}_level") * multiplier;
                foreach (var idea in ideaGroup.Ideas)
                {
                    if (!weight.ContainsKey(idea))
                    {
                        weight[idea] = 0;
                    }
                    if (!level.ContainsKey(idea))
                    {
                        level[idea] = 0;
                    }
                    weight[idea] += GetFloatEffect(effects, $"idea_{idea.Name}_weight") * multiplier + groupWeight;
                    level[idea] += GetFloatEffect(effects, $"idea_{idea.Name}_level") * multiplier + groupLevel;
                }
			}

			foreach (var gov in World.GovernmentTypes)
			{
				if (!GovernmentTypeWeights.ContainsKey(gov))
				{
					GovernmentTypeWeights[gov] = 0;
				}
				GovernmentTypeWeights[gov] += GetFloatEffect(effects, $"gov_{gov}") * multiplier;
            }

            foreach (var reform in World.GovernmentReforms)
            {
                if (!GovernmentReformWeights.ContainsKey(reform))
                {
                    GovernmentReformWeights[reform] = 0;
                }
                GovernmentReformWeights[reform] += GetFloatEffect(effects, $"reform_{reform}") * multiplier;
            }

			VassalThreshhold += GetFloatEffect(effects, "vassal_threshhold") * multiplier;

            // renaissance
            RenaissanceFactor += GetFloatEffect(effects, "renaissance_factor") * multiplier;
		}

		public void CalcEffectsHolding(PdxSublist effects)
		{
			CalcEffects(effects, IdeaWeightsHolding, IdeaLevelsHolding);

		}

		public void CalcEffectsProvince(PdxSublist effects)
		{
			CalcEffects(effects, IdeaWeightsProvince, IdeaLevelsProvince);

		}

		private float GetFloatEffect(PdxSublist effects, string key)
		{
			if (!effects.FloatValues.ContainsKey(key))
			{
				return 0;
			}
			return effects.FloatValues[key].Sum();
		}

		public PdxSublist GetNationalIdeas()
		{
			//transfer holding effects into main effects
			TransferDict(IdeaWeightsHolding, IdeaWeights, 1f / NumHoldings);
			TransferDict(IdeaLevelsHolding, IdeaLevels, 1f / NumHoldings);

			TransferDict(IdeaWeightsProvince, IdeaWeights, 1f / NumProvinces);
			TransferDict(IdeaLevelsProvince, IdeaLevels, 1f / NumProvinces);

			var data = new PdxSublist();

			var trigger = new PdxSublist();
			trigger.KeyValuePairs.Add("tag", CountryTag);

			data.AddSublist("trigger", trigger);
			data.AddValue("free", "yes");
            var ideaCandidates = IdeaWeights.GroupBy(idea => idea.Key.Group).Select(ig => new NationalIdeaGroupScore(ig.Key, ig.Sum(id => id.Value) / ig.Count())).OrderByDescending(id => id.Score).ToList();//.ThenByDescending(id => IdeaLevels[id.Key]);
            NationalIdeas = new List<NationalIdea>();
            while(NationalIdeas.Count < 10)
            {
                var firstGroup = ideaCandidates.First();
                var groupIdeas = firstGroup.Group.Ideas.OrderByDescending(id => IdeaWeights[id]).ThenByDescending(id => IdeaLevels[id]).ToList();
                var index = 0;
                for (index = 0; index < groupIdeas.Count && NationalIdeas.Contains(groupIdeas[index]); index++) ; // if it already exists, skip this one

                if (index < groupIdeas.Count)
                {
                    NationalIdeas.Add(groupIdeas[index]);
                } else
                {
                    firstGroup.Score = -1000;
                }
                firstGroup.Score /= 2;
                ideaCandidates = ideaCandidates.OrderByDescending(ic => ic.Score).ToList();
            }
            //NationalIdeas = ideaCandidates.Take(10).Select(id => id.Key).ToList();


			//tradition
			var traditionData = new PdxSublist();
			TransferEffects(traditionData, NationalIdeas[0]);
			TransferEffects(traditionData, NationalIdeas[1]);

			data.AddSublist($"start", traditionData);
			//ideas
			for (var i = 2; i < NationalIdeas.Count - 1; i++)
			{
				var idea = NationalIdeas[i];
				var ideaData = new PdxSublist();

				var level = TransferEffects(ideaData, idea);

				data.AddSublist($"{idea.Name}_{level}", ideaData);
			}

			//ambition
			var ambitionData = new PdxSublist();
			TransferEffects(ambitionData, NationalIdeas.Last());

			data.AddSublist($"bonus", ambitionData);

			if (title.Name == "k_dyn_8040718")
			{

				foreach (var idea in NationalIdeas)
				{
					var level = Math.Max(0, Math.Min((int)IdeaLevels[idea], idea.Effects.Count - 1));
					Console.WriteLine($"{idea.Name} x{level} ({IdeaWeights[idea]})");
				}

				Console.WriteLine();
			}
			return data;
		}

		private int TransferEffects(PdxSublist ideaData, NationalIdea idea)
		{
			if (title.Name == "k_dyn_8040718")
			{
				Console.WriteLine();
			}
			var level = Math.Max(0, Math.Min((int)IdeaLevels[idea], idea.Effects.Count - 1));
			var effects = idea.Effects[level];
			foreach (var effect in effects.FloatValues)
			{
				ideaData.FloatValues.Add(effect.Key, effect.Value);
			}
			foreach (var effect in effects.BoolValues)
			{
				ideaData.BoolValues.Add(effect.Key, effect.Value);
			}

			return level;
		}

		/// <summary>
		/// Adds the contents of the from dictionary to the to dictionary, applying a multiplier first
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="from"></param>
		/// <param name="to"></param>
		/// <param name="multiplier"></param>
		private void TransferDict<T>(Dictionary<T, float> from, Dictionary<T, float> to, float multiplier)
		{
			foreach (var fromEntry in from)
			{
				if (!to.ContainsKey(fromEntry.Key))
				{
					to[fromEntry.Key] = 0;
				}
				to[fromEntry.Key] += fromEntry.Value * multiplier;
			}
		}

		public override PdxSublist GetCountryFile()
		{
			var data = new PdxSublist(null, FileName + ".txt");
			if (MapColour == null)
			{
				MapColour = title.Colour ?? new Colour(255, 255, 255);
			}
			var colour = new PdxSublist(null, "color");
			colour.AddValue(MapColour.Red.ToString());
			colour.AddValue(MapColour.Green.ToString());
			colour.AddValue(MapColour.Blue.ToString());

			data.AddSublist("color", colour);
			//TODO: Graphical culture
			data.AddValue("graphical_culture", "westerngfx");
			return data;
		}

		public override PdxSublist GetHistoryFile()
		{
			var data = new PdxSublist(null, (FileName == CountryTag ? CountryTag : (CountryTag + " - " + FileName)) + ".txt");

			data.AddValue("government", Government);
            foreach (var reform in Reforms)
            {
                data.AddValue("add_government_reform", reform);
            }
			data.AddValue("government_rank", GovernmentRank.ToString());
			data.AddValue("mercantilism", Mercantilism.ToString());
			//TODO: tech groups
			data.AddValue("technology_group", TechGroup);
			data.AddValue("religion", Religion);
			data.AddValue("primary_culture", PrimaryCulture);
			foreach (var culture in AcceptedCultures)
			{
				data.AddValue("add_accepted_culture", culture);
			}
			if (IsElector)
			{
				data.AddValue("elector", "yes");
			}
            if (Capital != 0)
            {
                data.AddValue("capital", Capital.ToString());
            }
			if (holder != null)
			{
				
				var rulerData = GetCharacterData("monarch", holder);
				
				
				//rulerData.AddValue("clear_scripted_personalities", "yes");
				data.AddSublist(rulerData.Key, rulerData);
				if(title.Name == "k_dyn_8040718")
				{
					Console.WriteLine();
				}
				var heir = holder.Heir;
				if(heir != null)
				{
					var heirData = GetCharacterData("heir", heir);
					data.AddSublist(heirData.Key, heirData);
				}
				//TODO: heirs
				//if(holder.PrimaryTitle.Succession == "succ_primogeniture")
				//{
				
				//}
			}
			//TODO: heirs & spouses

			return data;
		}

		private PdxSublist GetCharacterData(string type, CK2CharacterBase character)
		{
			var rulerData = new PdxSublist(null, character.BirthDate.ToString("yyyy.MM.dd"));
			var monarchData = new PdxSublist(null, type);
			monarchData.AddValue("name", character.Name);

			if (character.DynastyID == 0)
			{ //todo: better support for lowborn rulers
			  //monarchData.AddValue("dynasty", "Lowborn");
			}
			else {
				monarchData.AddValue("dynasty", character.World.CK2Dynasties[character.DynastyID].Name);
			}
            if(CountryTag == "SCA")
            {
                Console.WriteLine();
            }
			monarchData.AddValue("birth_date", rulerData.Key);
			monarchData.AddValue("adm", ((character.Attribites.Stewardship + character.Attribites.Learning) / 6).ToString());
			monarchData.AddValue("dip", ((character.Attribites.Diplomacy + character.Attribites.Learning) / 6).ToString());
			monarchData.AddValue("mil", ((character.Attribites.Martial + character.Attribites.Learning) / 6).ToString());
			
			if(type == "heir")
			{
				monarchData.AddValue("claim", "95");
				monarchData.AddValue("monarch_name", character.Name);
				monarchData.AddValue("death_date", "1821.5.5");
			}


			rulerData.AddSublist(monarchData.Key, monarchData);
			return rulerData;
		}

		public override void AddDiplomacy(PdxSublist data)
		{
			//vassals
			foreach (var sub in Subjects)
			{
				var subData = new PdxSublist();
				subData.AddValue("first", CountryTag);
				subData.AddValue("second", sub);

				subData.AddValue("start_date", World.StartDate);
				subData.AddValue("end_date", "1821.1.1");

				data.AddSublist("vassal", subData);
			}

			//HRE
			if (holder?.PrimaryTitle == World.HRE)
			{
				var hreData = new PdxSublist();
				hreData.AddValue("emperor", CountryTag);
				data.AddSublist(World.StartDate, hreData);

			}
		}

		internal void MakeVassalOf(Eu4Country eu4Country)
		{
            if(Overlord == eu4Country.CountryTag)
            {
                return;
            }
			Overlord = eu4Country.CountryTag;
			eu4Country.Subjects.Add(this.CountryTag);
		}

		public void AddLocalisation(Dictionary<string, string> localisation)
		{
			if (DisplayNoun != null)
			{
				localisation.Add(CountryTag, DisplayNoun);
			}
			var adj = DisplayAdj ?? DisplayNoun;
			
			localisation.Add($"{CountryTag}_ADJ", adj);
			localisation.Add($"{CountryTag}_ideas", $"{adj} ideas");
			localisation.Add($"{CountryTag}_ideas_start", $"{adj} traditions");
			localisation.Add($"{CountryTag}_ideas_bonus", $"{adj} ambitions");
			
		}

		public string GetFlagPath()
		{
			if (IsVanilla)
			{
				return null;
			}
			return title.Flag;

		}
	}
}
