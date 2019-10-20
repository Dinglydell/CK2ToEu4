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
using PdxUtil;

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
        // private readonly int CULTURE_SPLIT_THRESHHOLD = 10000;
        /// <summary>
        /// The proportion of provinces of a culture/group that need to be part of the same thing in order for the culture to be named after that thing
        /// </summary>
        private readonly float CULTURE_NAME_THRESHOLD = 0.8f;

        public Dictionary<int, ProvMap> ProvinceMapper { get; set; }

        //public Dictionary<string, string> GovernmentMapper { get; set; }
        //public Dictionary<string, string> TitleGovernmentMapper { get; private set; }

        public Dictionary<string, string> CountryMapper { get; set; }
        public Dictionary<string, string> CountryFileMapper { get; set; }

        public Dictionary<string, string> ReligionMapper { get; set; }
        public Dictionary<string, string> CultureMapper { get; set; }

        public Dictionary<string, string> RevoltMapper { get; set; }
        public PdxSublist TechGroups { get; private set; }
        public Dictionary<Eu4SuperRegion, string> SuperRegionTechGroup { get; set; }

        public CK2Save CK2World { get; private set; }
        public int NumCustomCountries { get; set; }
        public List<NationalIdeaGroup> NationalIdeaGroups { get; set; }
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
        public string RenaissanceRegion { get; private set; }
        public CK2Title HRE { get; internal set; }
        public HashSet<string> TakenTags { get; private set; }
        public string StartDate { get { return CK2World.KeepStartDate ? CK2World.Date : "1444.11.1"; } }

        public Dictionary<CK2Title, Formable> Formables { get; private set; }
        /// <summary>
        /// Maps CK2 province IDs to EU4 cultures
        /// </summary>
        public Dictionary<int, Eu4Culture> CultureProvMapper { get; private set; }

        public Eu4World(CK2Save ck2World) : base("")
        {
            DeleteDirectoryRecursive("output");
            CK2World = ck2World;
            CK2World.Eu4World = this;

            Countries = new Dictionary<string, Eu4CountryBase>();
            TakenTags = new HashSet<string>();
            if (CK2World.CK2Titles.ContainsKey("e_hre"))
            {
                HRE = CK2World.CK2Titles["e_hre"];
            }
            Console.WriteLine("Constructing EU4 world...");

            LoadWorldMap();
            LoadMappers();
            CK2World.LoadWars();
            LoadEffects();
            PostInitLoad();
            DoCultureSplit();
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
            HandleReformedPagans();
            WriteMod();
        }

        private void HandleReformedPagans()
        {
            var paganEffects = PdxSublist.ReadFile("paganEffects.txt");

            foreach (var rel in CK2World.CK2Religions)
            {
                if(rel.Key == "west_african_pagan_reformed")
                {
                    Console.WriteLine();
                }
                if (rel.Value.Features != null)
                {
                    var eu4Rel = MapReligion(rel.Value);
                    foreach (var feature in rel.Value.Features)
                    {
                        eu4Rel.AddEffects(paganEffects.Sublists[feature]);
                    }
                }
            }
        }

        /// <summary>
        /// Throws all EU4 Cultures out the window and generates an entirely new set of cultures based on the distribution of provinces, religions, realms and CK2 cultures
        /// </summary>
        private void DoCultureSplit()
        {
            CultureProvMapper = new Dictionary<int, Eu4Culture>();
            Console.WriteLine("Generating cultures...");
            var cultures = new List<CohesiveSet<CK2Province>>();

            var groupedProvs = CK2World.CK2Provinces.GroupBy(p => p.Value.Culture.Group);
            Parallel.ForEach(groupedProvs, gp =>
            {
                Console.WriteLine($"Generating {gp.Key.Name} cultures...");
                var cultureGenerator = new SetGenerator<CK2Province>(gp.Select(p => p.Value), (provA, provB) =>
                {
                    var dx = (provA.MapPosition.X - provB.MapPosition.X);
                    var dy = (provA.MapPosition.Y - provB.MapPosition.Y);
                    var distance = (int)Math.Sqrt(dx * dx + dy * dy) / 100;
                    foreach (TitleRank rank in Enum.GetValues(typeof(TitleRank)))
                    {
                        if (TitleRank.barony == rank)
                        {
                            continue;
                        }
                        if (provA.CountyTitle.GetLiege(rank) != provB.CountyTitle.GetLiege(rank))
                        {
                            distance += 10 * (int)rank;
                        }
                        if (provA.CountyTitle.GetDejureLiege(rank) != provB.CountyTitle.GetDejureLiege(rank))
                        {
                            distance += 5 * (int)rank;
                        }

                    }
                    if (provA.Religion != provB.Religion)
                    {
                        distance += 40;
                    }
                    if (provA.Culture != provB.Culture)
                    {
                        distance += 80;
                    }
                    if (provA.Culture.Group != provB.Culture.Group)
                    {
                        distance += 160;
                    }

                    return distance;
                });
                var groupCultures = cultureGenerator.GenerateSets(1200);
                lock (cultures)
                {
                    cultures.AddRange(groupCultures);
                }
                Console.WriteLine($"Created {groupCultures.Count} cultures for {gp.Key.Name}.");
            });
            Console.WriteLine("Grouping cultures...");
            var groupGenerator = new SetGenerator<CohesiveSet<CK2Province>>(cultures, (setA, setB) =>
            {
                return setA.GetDistance(setA.CentralElement, setB.CentralElement);
            });
            var cultureGroups = groupGenerator.GenerateSets(2400);
            Console.WriteLine($"Generated {cultures.Count} cultures in {cultureGroups.Count} groups.");
            CultureGroups = new Dictionary<string, Eu4CultureGroup>();
            Cultures = new Dictionary<string, Eu4Culture>();
            var nonAlphanumeric = new Regex("[^a-zA-Z0-9-]");
            foreach (var group in cultureGroups)
            {
                //TODO: find a better naming system. Analyse group based on duchy, kingdom and empire titles to get the highest-level name possible
                //TODO: find ways of giving the culture other data such as names
                var groupDisplay = FindCulturalName(group);
                var groupCodeName = nonAlphanumeric.Replace(groupDisplay.ToLower(), "") + "_group";
                var eu4Group = new Eu4CultureGroup(groupCodeName);
                eu4Group.DisplayName = groupDisplay;
                CultureGroups[groupCodeName] = eu4Group;
                foreach (var cul in group.Content)
                {
                    var culDisplay = FindCulturalName(cul.Content, cul.CentralElement, groupDisplay);

                    var culName = nonAlphanumeric.Replace(culDisplay.ToLower(), "");

                    if (Cultures.ContainsKey(culName))
                    {
                        if (!Cultures[culName].IsVanilla) {
                            culDisplay = $"{cul.CentralElement.CountyTitle.DisplayAdj} {culDisplay}";
                        } 
                        culName = $"{culName}_{cul.CentralElement.CountyTitle.Name}";
                    }
                    var eu4Culture = eu4Group.AddCulture(culName, this, false, culDisplay);

                    Cultures[culName] = eu4Culture;
                    foreach (var prov in cul.Content)
                    {
                        CultureProvMapper[prov.ID] = eu4Culture;
                    }
                }
            }
        }
        /// <summary>
        /// Finds an appropriate name for a culture group
        /// </summary>
        /// <param name="group"></param>
        /// <returns></returns>
        private string FindCulturalName(CohesiveSet<CohesiveSet<CK2Province>> group)
        {

            var flatList = group.Content.SelectMany(s => s.Content);
            return FindCulturalName(flatList, group.CentralElement.CentralElement);
        }
        /// <summary>
        /// Finds an appropriate name for a culture/culture group
        /// </summary>
        /// <param name="flatList"></param>
        /// <param name="centre"></param>
        /// <param name="exclude">will not use this as a name</param>
        /// <returns></returns>
        private string FindCulturalName(IEnumerable<CK2Province> flatList, CK2Province centre, string exclude = null)
        {
            //1. Look at ck2 cultures that our provinces are from, if any ck2 culture makes up the overwhelming majority then use their name
            //2. Repeat above for ck2 culture groups
            //3. Check if supermajority of group is in the same dejure duchy, if so name after duchy
            //4. Repeat above for defacto duchy
            //5. Repeat above for dejure kingdom
            //6. Repeat above for defacto kingdom
            //7. Repeat above for dejure empire
            //8. Repeat above for defacto empire


            //1 - checking CK2 cultures
            var ck2CulCheck = centre.Culture;
            //if a supermajority of this culture's provinces are of this CK2Culture
            if (ck2CulCheck.DisplayName != exclude && HasSupermajority(flatList, CULTURE_NAME_THRESHOLD, p => p.Culture == ck2CulCheck)
                // and a supermajority of the CK2Culture's provinces are of this culture
                && HasSupermajority(CK2World.CK2Provinces.Where(p => p.Value.Culture == ck2CulCheck), CULTURE_NAME_THRESHOLD, p => flatList.Contains(p.Value)))
            {
                return ck2CulCheck.DisplayName;
            }

            //2 - check CK2 culture groups
            var ck2CulGroupCheck = ck2CulCheck.Group;
            var candidate = CK2World.Localisation[ck2CulGroupCheck.Name];
            //if a supermajority of this culture's provinces are of this CK2CultureGroup
            if (exclude != null && candidate != exclude && HasSupermajority(flatList, CULTURE_NAME_THRESHOLD, p => p.Culture.Group == ck2CulGroupCheck)
                //and a supermajority of the CK2CultureGroup's provinces are of this culture
                && HasSupermajority(CK2World.CK2Provinces.Where(p => p.Value.Culture.Group == ck2CulGroupCheck), CULTURE_NAME_THRESHOLD, p => flatList.Contains(p.Value)))
            {
                return candidate;
            }
            var county = centre.CountyTitle;
            //3-8 - check dejure/defacto titles from duchy to empire

            var dejureTitles = new CK2Title[] {
                county.GetDejureLiege(TitleRank.duchy),
                county.GetDejureLiege(TitleRank.kingdom),
                county.GetDejureLiege(TitleRank.empire),
            };
            var defactoTitles = new CK2Title[] {
                county.GetLiege(TitleRank.duchy),
                county.GetLiege(TitleRank.kingdom),
                county.GetLiege(TitleRank.empire),
            };
            for (var i = 0; i < dejureTitles.Length; i++)
            {
                //check dejure title
                // if a supermajority of the culture's provinces are dejurically in this title
                if (dejureTitles[i] != null && dejureTitles[i].DisplayAdj != exclude && HasSupermajority(flatList, CULTURE_NAME_THRESHOLD, p => p.CountyTitle.IsDejureVassalOf(dejureTitles[i]))
                    // and a supermajority of the title's dejure provinces are this culture
                    && HasSupermajority(CK2World.CK2Provinces.Where(p => p.Value.CountyTitle.IsDejureVassalOf(dejureTitles[i])), CULTURE_NAME_THRESHOLD, p => flatList.Contains(p.Value)))
                {
                    return dejureTitles[i].DisplayAdj ?? dejureTitles[i].DisplayName;
                }

                //check defacto title
                // if a supermajority of the culture's provinces are defacto in this title
                if (defactoTitles[i] != null && defactoTitles[i].DisplayAdj != exclude && HasSupermajority(flatList, CULTURE_NAME_THRESHOLD, p => p.CountyTitle.IsVassalOf(defactoTitles[i]))
                    // and a supermajority of this title's defacto provinces are this culture
                    && HasSupermajority(CK2World.CK2Provinces.Where(p => p.Value.CountyTitle.IsVassalOf(defactoTitles[i])), CULTURE_NAME_THRESHOLD, p => flatList.Contains(p.Value)))
                {
                    return defactoTitles[i].DisplayAdj ?? defactoTitles[i].DisplayName;
                }

            }


            return county.DisplayAdj;
        }

        /// <summary>
        /// Returns whether or not the number of items in a list that satisfy a condition is above a certain proportion of the total list
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="threshold"></param>
        /// <param name="condition"></param>
        /// <returns></returns>
        private bool HasSupermajority<T>(IEnumerable<T> list, float threshold, Func<T, bool> condition)
        {
            return list.Count() * threshold < list.Where(condition).Count();
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
            // assign tech groups to superregions
            var techGroupMap = PdxSublist.ReadFile("tech_groups.txt");
            SuperRegionTechGroup = new Dictionary<Eu4SuperRegion, string>();
            foreach (var sr in techGroupMap.KeyValuePairs)
            {
                SuperRegionTechGroup[SuperRegions[sr.Key]] = sr.Value;
                //n += techiest.Count() / techGroupMap.Length;
            }
            // calculate tech value each tech group should have
            TechGroups = PdxSublist.ReadFile(GetFilesFor("common").Where(p => Path.GetFileName(p) == "technology.txt").Single());
            //total tech of provinces in this group
            var groupTech = new Dictionary<string, float>();
            //total number of provinces in this group
            var groupProvs = new Dictionary<string, int>();
            foreach (var sr in SuperRegions)
            {
                if (SuperRegionTechGroup.ContainsKey(sr.Value))
                {
                    var techGroup = SuperRegionTechGroup[sr.Value];
                    if (!groupTech.ContainsKey(techGroup))
                    {
                        groupTech[techGroup] = 0;
                    }
                    if (!groupProvs.ContainsKey(techGroup))
                    {
                        groupProvs[techGroup] = 0;
                    }
                    groupTech[techGroup] += sr.Value.Regions == null ? 0 : sr.Value.Regions.Sum(r => r.Areas == null ? 0 : r.Areas.Sum(a => a.Provinces.Sum(p => ((Eu4Province)Provinces[p]).TotalTech)));
                    groupProvs[techGroup] += sr.Value.Regions.Sum(r => r.Areas == null ? 0 : r.Areas.Sum(a => a.Provinces.Where(p => (((Eu4Province)Provinces[p]).CK2Titles?.Count ?? 0) != 0).Count()));
                }
            }
            // tech per province in each group, in order from most tech to least
            var techiest = groupTech.Where(r => !r.Key.Contains("sea") && r.Value > 0).Select(p => new KeyValuePair<string, float>(p.Key, p.Value / groupProvs[p.Key])).OrderByDescending(r => r.Value);

            Console.WriteLine($"The techiest group is {techiest.First().Key}. ({techiest.First().Value})");
            //TODO: consider making this not a burried magic number
            var highestLevel = 75;//techiest.First().Value;

            foreach (var tech in techiest)
            {
                var groupSub = TechGroups.Sublists["groups"].Sublists[tech.Key];
                var level = (int)(3 * tech.Value / highestLevel);
                //TODO: create a better way of editting float values
                groupSub.FloatValues["start_level"][0] = level;
                Console.WriteLine($"Tech group {tech.Key} had level {tech.Value} in CK2 so starts with tech level {level}.");
            }
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


            Directory.CreateDirectory("output");

            var ck2Name = Path.GetFileNameWithoutExtension(CK2World.FilePath);
            var modFolder = $"converted_{ck2Name}";

            //.mod file
            Console.WriteLine("Generating .mod file...");
            var modFile = File.ReadAllText(@"template\modfile.mod").Replace("$NAME$", $"Converted - {ck2Name}").Replace("$FOLDER$", modFolder);
            File.WriteAllText($@"output\{modFolder}.mod", modFile);

            Console.WriteLine("Copying mod template...");
            var modPath = $@"output\{modFolder}";
            CopyDirectory(@"template\copy", modPath);
            Console.WriteLine("Writing EU4 mod files...");
            Directory.CreateDirectory($@"{modPath}\history");
            Directory.CreateDirectory($@"{modPath}\history\provinces");
            Console.WriteLine("Writing province history...");
            foreach (var prov in Provinces)
            {
                var history = prov.Value.GetHistoryFile();
                if (prov.Value.FileName != null)
                {
                    using (var file = new StreamWriter($@"{modPath}\history\provinces\{prov.Value.FileName}.txt"))
                    {
                        history.WriteToFile(file);
                    }
                }

            }
            Directory.CreateDirectory($@"{modPath}\common");
            Directory.CreateDirectory($@"{modPath}\common\religions");

            //religion
            Console.WriteLine("Writing reformed pagan religions...");
            var religionsData = GetFilesFor(@"common\religions").Select(rf => PdxSublist.ReadFile(rf));
            var editedFiles = new HashSet<PdxSublist>();
            foreach (var rel in Religions)
            {
                if (rel.Value.Effects != null)
                {
                    // find the file that has this relgion group in it
                    var editCandidates = editedFiles.Where(r => r.Sublists.ContainsKey(rel.Value.Group.Name));
                    var groupFile = editCandidates.FirstOrDefault() ?? religionsData.Where(r => r.Sublists.ContainsKey(rel.Value.Group.Name)).Single();
                    rel.Value.Effects.AddValue("icon", rel.Value.Icon.ToString());
                    var colData = new PdxSublist();
                    colData.AddValue(rel.Value.Colour.Red.ToString());
                    colData.AddValue(rel.Value.Colour.Green.ToString());
                    colData.AddValue(rel.Value.Colour.Blue.ToString());
                    rel.Value.Effects.AddSublist("color", colData);
                    groupFile.Sublists[rel.Value.Group.Name].Sublists[rel.Key] = rel.Value.Effects;
                    
                    editedFiles.Add(groupFile);
                }
            }
            foreach (var file in editedFiles)
            {
                using (var stream = new StreamWriter($@"{modPath}\common\religions\{Path.GetFileName(file.Key)}"))
                {
                    file.WriteToFile(stream);
                }
            }

            Directory.CreateDirectory($@"{modPath}\common\countries");
            Directory.CreateDirectory($@"{modPath}\common\country_tags");

            Directory.CreateDirectory($@"{modPath}\history\countries");
            Directory.CreateDirectory($@"{modPath}\gfx");
            Directory.CreateDirectory($@"{modPath}\gfx\flags");

            Directory.CreateDirectory($@"{modPath}\common\ideas");
            Directory.CreateDirectory($@"{modPath}\history\diplomacy");

            //tech groups
            Console.WriteLine("Writing technology.txt...");
            using (var file = new StreamWriter($@"{modPath}\common\technology.txt"))
            {
                TechGroups.WriteToFile(file);
            }
            // countries
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


                    using (var file = new StreamWriter($@"{modPath}\common\countries\{countryData.Key}"))
                    {
                        countryData.WriteToFile(file);
                    }

                    //country flag
                    var flag = ((Eu4Country)country.Value).GetFlagPath();
                    if (flag != null)
                    {
                        File.Copy(flag, $@"{modPath}\gfx\flags\{country.Value.CountryTag}.tga");
                    }


                }
                var historyData = country.Value.GetHistoryFile();

                using (var file = new StreamWriter($@"{modPath}\history\countries\{historyData.Key}"))
                {
                    historyData.WriteToFile(file);
                }

                var ideaData = ((Eu4Country)country.Value).GetNationalIdeas();

                ideas.AddSublist($"{country.Value.CountryTag}_ideas", ideaData);

                country.Value.AddDiplomacy(diplomacy);
                //todo: national ideas
            }
            //put country tags in file
            using (var file = new StreamWriter($@"{modPath}\common\country_tags\{tags.Key}"))
            {
                tags.WriteToFile(file);
            }
            //put national ideas in file
            using (var file = new StreamWriter($@"{modPath}\common\ideas\{ideas.Key}"))
            {
                ideas.WriteToFile(file);
            }
            //put diplomacy in file
            using (var file = new StreamWriter($@"{modPath}\history\diplomacy\{diplomacy.Key}"))
            {
                diplomacy.WriteToFile(file);
            }

            //culture
            Directory.CreateDirectory($@"{modPath}\common\cultures");
            var cultureData = new PdxSublist();

            foreach (var cul in CultureGroups)
            {
                if (cul.Value.AnyNew)
                {
                    cultureData.AddSublist(cul.Key, cul.Value.GetGroupData());
                }
            }
            using (var file = new StreamWriter($@"{modPath}\common\cultures\converted_cultures.txt"))
            {
                cultureData.WriteToFile(file);
            }


            //institutions

            var instFile = File.ReadAllText(@"template\institutions.txt").Replace("%HIGHEST_TECH_REGION%", RenaissanceRegion);
            Directory.CreateDirectory($@"{modPath}\common\institutions");
            File.WriteAllText($@"{modPath}\common\institutions\00_Core.txt", instFile);



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
            foreach (var idea in NationalIdeaGroups)
            {
                idea.AddLocalisation(locale);
            }
            foreach (var form in Formables)
            {
                form.Value.AddLocalisation(locale);
            }

            Directory.CreateDirectory($@"{modPath}\localisation");
            using (var file = new StreamWriter($@"{modPath}\localisation\zz_converted_l_english.yml", false, new UTF8Encoding(true)))
            {
                file.WriteLine("l_english:");
                foreach (var l in locale)
                {
                    file.Write(" ");
                    file.WriteLine($"{l.Key}:0 \"{l.Value}\"");
                }
            }

            //decisions
            Directory.CreateDirectory($@"{modPath}\decisions");
            var formableDecisionsFile = new PdxSublist(null, $@"{modPath}\decisions\converted_formable_nations.txt");
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

            WriteDefines(modPath);

            Directory.CreateDirectory($@"{modPath}\common\technologies");
            FixTech(modPath);



        }

        private void FixTech(string modPath)
        {
            if (CK2World.KeepStartDate)
            {
                var admTech = PdxSublist.ReadFile($@"{modPath}\common\technologies\adm.txt");
                var dipTech = PdxSublist.ReadFile($@"{modPath}\common\technologies\dip.txt");
                var milTech = PdxSublist.ReadFile($@"{modPath}\common\technologies\mil.txt");

                var startYear = int.Parse(StartDate.Substring(0, StartDate.IndexOf('.')));
                FixTech(admTech, startYear);
                FixTech(dipTech, startYear);
                FixTech(milTech, startYear);

                using (var admFile = new StreamWriter($@"{modPath}\common\technologies\adm.txt"))
                {
                    admTech.WriteToFile(admFile);
                }
                using (var dipFile = new StreamWriter($@"{modPath}\common\technologies\dip.txt"))
                {
                    dipTech.WriteToFile(dipFile);
                }
                using (var milFile = new StreamWriter($@"{modPath}\common\technologies\mil.txt"))
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

        private void WriteDefines(string modPath)
        {
            if (CK2World.KeepStartDate)
            {
                Directory.CreateDirectory($@"{modPath}\common\defines");

                using (var file = new StreamWriter($@"{modPath}\common\defines\converted_defines.lua"))
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
            var regionRenaissanceFactor = Countries.Where(c => c.Value.Capital != 0).GroupBy(c => Provinces[c.Value.Capital].Area.Region).Select(g => new KeyValuePair<string, float>(g.Key.Name, g.Sum(c => ((Eu4Country)c.Value).RenaissanceFactor) / (float)Math.Sqrt((g.Key.Areas.Sum(a => a.Provinces.Count) * g.Count())) ));
            var superregionRenaissanceFactor = Countries.Where(c => c.Value.Capital != 0 && Provinces[c.Value.Capital].Area.Region.SuperRegion != null).GroupBy(c => Provinces[c.Value.Capital].Area.Region.SuperRegion).Select(g => new KeyValuePair<string, float>(g.Key.Name, g.Sum(c => ((Eu4Country)c.Value).RenaissanceFactor) / (g.Key.Regions.Sum(r => r.Areas.Sum(a => a.Provinces.Count)) * g.Count())));
            var renaissanceOrder = regionRenaissanceFactor.OrderByDescending(r => r.Value);
            var superregionOrder = superregionRenaissanceFactor.OrderByDescending(r => r.Value);
            RenaissanceRegion = renaissanceOrder.First().Key;
            Console.WriteLine("The renaissance region is " + RenaissanceRegion);



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

        //TODO: rethink how the calculation for generating a vassal should work
        public Eu4Country GetIndependentCountryFromCharacter(CK2Character holder)
        {
            if (holder == null)
            {
                return null;
            }
            if (holder.ID == 1549424)
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
                    if (holder.Religion != holder.Liege.Religion)
                    {
                        vassalThreshhold -= 0.075f;
                    }
                    if (holder.Culture != holder.Liege.Culture)
                    {
                        vassalThreshhold -= 0.075f;
                    }
                    if (!holder.IsDejureVassalOf(holder.Liege))
                    {
                        vassalThreshhold -= 0.075f;
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
            return CultureProvMapper[province.ID];
            //var cul = CultureMapper[province.Culture.Name];

            //var distance = province.Culture.GetDistanceTo(province);
            //var threshhold = CULTURE_SPLIT_THRESHHOLD;
            //var culture = province.Culture;
            //if (distance > threshhold)
            //{
            //	CK2Culture closest = null;
            //	var closeDist = int.MaxValue;
            //	foreach (var sub in province.Culture.SubCultures)
            //	{
            //		var dist = sub.GetDistanceTo(province);
            //		if (dist < closeDist && dist < threshhold)
            //		{
            //			closeDist = dist;
            //			closest = sub;
            //		}
            //	}
            //	if (closest == null)
            //	{
            //		var sub = province.Culture.CreateSubCulture(province);
            //		culture = sub;
            //	}
            //	else
            //	{
            //		cul = closest.Name;
            //		culture = closest;
            //	}
            //}

            //if (Cultures.ContainsKey(cul))
            //{
            //	return Cultures[cul];
            //}

            ////TODO: splitter logic - also things such as anglo-saxon culture group
            //if (CultureGroups.ContainsKey(cul))
            //{
            //	return CultureGroups[cul].Cultures[0];
            //}


            ////create new
            //Eu4CultureGroup group;
            //if (culture.IsSubCulture)
            //{
            //	var parent = CultureMapper[culture.Parent.Name];
            //	if (Cultures.ContainsKey(parent))
            //	{
            //		group = Cultures[parent].Group;
            //	}
            //	else if (CultureGroups.ContainsKey(parent))
            //	{
            //		group = CultureGroups[parent];

            //	}
            //	else
            //	{
            //		group = new Eu4CultureGroup(parent);
            //		CultureGroups[parent] = group;
            //	}

            //}
            //else
            //{
            //	CultureGroups[cul] = new Eu4CultureGroup(cul);
            //	group = CultureGroups[cul];
            //}
            //Cultures[cul] = group.AddCulture(cul, this, false, culture.DisplayName);

            //if (culture.IsSubCulture)
            //{
            //	var parent = CultureMapper[culture.Parent.Name];
            //	if (Cultures.ContainsKey(parent))
            //	{
            //		if (string.IsNullOrEmpty(culture.Centre.CountyTitle.DisplayAdj ?? culture.Centre.CountyTitle.DisplayName) || string.IsNullOrEmpty(Cultures[parent].DisplayName))
            //		{
            //			Console.WriteLine();
            //		}
            //		Cultures[cul].DisplayName = culture.Centre.CountyTitle.DisplayAdj ?? culture.Centre.CountyTitle.DisplayName + "-" + Cultures[parent].DisplayName;
            //	}
            //	else
            //	{
            //		Cultures[cul].DisplayName = culture.Centre.CountyTitle.DisplayAdj;
            //	}
            //}

            //return Cultures[cul];
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
            NationalIdeaGroups = new List<NationalIdeaGroup>();
            var nis = PdxSublist.ReadFile("nationalIdeas.txt");

            nis.ForEachSublist(sub =>
            {
                NationalIdeaGroups.Add(new NationalIdeaGroup(sub.Value));
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
                    foreach (var prov in Areas[ar.Key].Provinces)
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
