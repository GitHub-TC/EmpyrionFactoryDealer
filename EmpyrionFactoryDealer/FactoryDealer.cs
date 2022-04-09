using Eleon.Modding;
using EmpyrionNetAPIAccess;
using EmpyrionNetAPIDefinitions;
using EmpyrionNetAPITools;
using EmpyrionNetAPITools.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace EmpyrionGalaxyNavigator
{
    public class FactoryDealer : EmpyrionModBase
    {
        public ModGameAPI GameAPI { get; set; }

        public ConfigurationManager<Configuration> Configuration { get; set; }

        public IReadOnlyDictionary<string, int> BlockNameIdMapping
        {
            get
            {
                if (_BlockNameIdMapping == null && File.Exists(Configuration.Current.NameIdMappingFile ?? string.Empty))
                    Log($"NameIdMapping:'{Configuration.Current.NameIdMappingFile}' CurrentDirectory:{Directory.GetCurrentDirectory()}", LogLevel.Message);
                try { _BlockNameIdMapping = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(Configuration.Current.NameIdMappingFile)); }
                catch (Exception error) { Log($"NameIdMapping read failed:{error}", LogLevel.Error); }

                return _BlockNameIdMapping;
            }
        }
        IReadOnlyDictionary<string, int> _BlockNameIdMapping;

        public FactoryDealer()
        {
            EmpyrionConfiguration.ModName = "EmpyrionFactoryDealer";
        }

        public override void Initialize(ModGameAPI dediAPI)
        {
            GameAPI = dediAPI;

            try
            {
                Log($"**EmpyrionFactoryDealer loaded: {string.Join(" ", Environment.GetCommandLineArgs())}", LogLevel.Message);

                LoadConfiguration();
                LogLevel = Configuration.Current.LogLevel;
                ChatCommandManager.CommandPrefix = Configuration.Current.ChatCommandPrefix;

                ChatCommands.Add(new ChatCommand(@"factory help",   (I, A) => DisplayHelp           (I.playerId), "display help"));
                ChatCommands.Add(new ChatCommand(@"factory finish", (I, A) => FinishedFactory       (I.playerId), "finish factory blueprint"));
                ChatCommands.Add(new ChatCommand(@"factory buyall", (I, A) => RebuyAllRessources    (I.playerId), "rebuy all ressources from factory"));
                ChatCommands.Add(new ChatCommand(@"factory getall", (I, A) => ExtractAllRessources  (I.playerId), "extract all ressources from factory"));
            }
            catch (Exception Error)
            {
                Log($"**EmpyrionFactoryDealer Error: {Error} {string.Join(" ", Environment.GetCommandLineArgs())}", LogLevel.Error);
            }
        }

        private async Task ExtractAllRessources(int playerId)
        {
            var P = await Request_Player_Info(playerId.ToId());
            if (P.bpResourcesInFactory == null || P.bpResourcesInFactory.Count == 0)
            {
                InformPlayer(playerId, $"You have no ressources in the factory.");
                return;
            }

            if (!string.IsNullOrEmpty(P.bpInFactory) && P.bpRemainingTime > 0)
            {
                InformPlayer(playerId, $"You have a building blueprint in the factory.");
                return;
            }

            var answer = await ShowDialog(playerId, P, "Extract ressources from factory", $"Do you want to extract your ressources?" +
                P.bpResourcesInFactory.Aggregate("\n", (l, r) => l + $"[c][f0ff00]{(int)(r.Value * (1.0 - ExtractionPercentLoss(r.Key)))}[-][/c] of [c][00ff00]{GetItemName(r.Key)}[-][/c] loss [c][ff0000]{(int)(r.Value * ExtractionPercentLoss(r.Key))}[-][/c] = [c][ff0000]{ExtractionPercentLoss(r.Key):P1}[-][/c]\n"), 
                "Yes", "No");
            if (answer.Id != P.entityId || answer.Value != 0) return;

            var remainingItemStacks = new List<ItemStack>();
            foreach (var res in P.bpResourcesInFactory)
            {
                try
                {
                    await Request_Player_AddItem(new IdItemStack { id = playerId, itemStack = new ItemStack { id = res.Key, count = (int)(res.Value * (1.0 - ExtractionPercentLoss(res.Key)))} });
                }
                catch
                {
                    remainingItemStacks.Add(new ItemStack() { id = res.Key, count= (int)res.Value });
                }
            }

            await Request_Blueprint_Resources(new BlueprintResources { PlayerId = playerId, ItemStacks = remainingItemStacks, ReplaceExisting = true });

            if(remainingItemStacks.Count > 0)
            {
                await ShowDialog(playerId, P, "Extract ressources from factory", $"Not enough free spaces in the inventory\nResources remaining in the factory:\n" +
                    remainingItemStacks.Aggregate("\n", (l, r) => l + $"[c][f0ff00]{(int)r.count}[-][/c] of [c][00ff00]{GetItemName(r.id)}[-][/c]\n"),
                    "Ok", null);
            }
        }

        private string GetItemName(int itemId)
        {
            var foundName = Configuration.Current.Ressources?.FirstOrDefault(r => r.Item == itemId)?.Name;
            if(!string.IsNullOrEmpty(foundName)) return foundName;

            var foundId = BlockNameIdMapping?.FirstOrDefault(i => i.Value == itemId);
            return string.IsNullOrEmpty(foundId?.Key) ? itemId.ToString() : foundId?.Key;
        }

        private double ExtractionPercentLoss(int itemId)
            => Configuration.Current.Ressources.FirstOrDefault(res => res.Item == itemId)?.ExtractionPercentLost ?? Configuration.Current.ExtractionPercentLost;

        private async Task RebuyAllRessources(int playerId)
        {
            var P = await Request_Player_Info(playerId.ToId());
            if (P.bpResourcesInFactory == null || P.bpResourcesInFactory.Count == 0)
            {
                InformPlayer(playerId, $"You have no ressources in the factory.");
                return;
            }

            if (!string.IsNullOrEmpty(P.bpInFactory) && P.bpRemainingTime > 0)
            {
                InformPlayer(playerId, $"You have a building blueprint in the factory.");
                return;
            }

            var costs = P.bpResourcesInFactory.Aggregate(0.0, (s, r) => r.Value * (Configuration.Current.Ressources.FirstOrDefault(res => res.Item == r.Key)?.RebuyCostPerUnit ?? Configuration.Current.RebuyCostPerUnit) + s);
            if (P.credits < costs)
            {
                InformPlayer(playerId, $"You need {costs} credits to rebuy the ressources from the factory.");
                return;
            }

            var answer = await ShowDialog(playerId, P, "Rebuy ressources from factory", $"Do you want to rebuy your ressources for [c][00ff00]{costs}[-][/c] credits?", "Yes", "No");
            if (answer.Id != P.entityId || answer.Value != 0) return;

            costs = 0;
            var remainingItemStacks = new List<ItemStack>();
            foreach (var res in P.bpResourcesInFactory)
            {
                try
                {
                    await Request_Player_AddItem(new IdItemStack { id = playerId, itemStack = new ItemStack { id = res.Key, count = (int)res.Value } });
                    costs += res.Value * (Configuration.Current.Ressources.FirstOrDefault(r => r.Item == res.Key)?.RebuyCostPerUnit ?? Configuration.Current.RebuyCostPerUnit);
                }
                catch
                {
                    remainingItemStacks.Add(new ItemStack() { id = res.Key, count = (int)res.Value });
                }
            }

            await Request_Blueprint_Resources(new BlueprintResources { PlayerId = playerId, ItemStacks = remainingItemStacks, ReplaceExisting = true });
            await Request_Player_AddCredits  (new IdCredits { id = playerId, credits = -costs });

            if (remainingItemStacks.Count > 0)
            {
                await ShowDialog(playerId, P, "Extract ressources from factory", $"Not enough free spaces in the inventory\n" +
                    $"you only have to pay [c][f0ff00]{costs}[-][/c] credits\nResources remaining in the factory:\n" +
                    remainingItemStacks.Aggregate("\n", (l, r) => l + $"[c][f0ff00]{(int)r.count}[-][/c] of [c][00ff00]{GetItemName(r.id)}[-][/c] loss [c][ff0000]{(int)(r.count * ExtractionPercentLoss(r.id))}[-][/c] = [c][ff0000]{ExtractionPercentLoss(r.id):P1}[-][/c]\n"),
                    "Ok", null);
            }
        }

        private async Task FinishedFactory(int playerId)
        {
            var P = await Request_Player_Info(playerId.ToId());
            if (string.IsNullOrEmpty(P.bpInFactory) || P.bpRemainingTime == 0)
            {
                InformPlayer(playerId, $"You have no blueprint in the factory to finished.");
                return;
            }

            var remainingTime = new TimeSpan((long)(P.bpRemainingTime * TimeSpan.TicksPerSecond));
            var costs         = (int)(remainingTime.TotalMinutes * Configuration.Current.CostsPerRemainingMinute);
            if (P.credits < costs)
            {
                InformPlayer(playerId, $"You need {costs} credits to finish the blueprint in the factory.");
                return;
            }

            if(costs == 0)
            {
                InformPlayer(playerId, $"The blueprint finished in the factory soon.");
                return;
            }

            var answer = await ShowDialog(playerId, P, "Finish factory blueprint", $"Do you want to finish your '[c][00ff00]{BpName(P.bpInFactory)}[-][/c]' blueprint for {costs} credits?", "Yes", "No");
            if (answer.Id != P.entityId || answer.Value != 0) return;

            await Request_Blueprint_Finish(P.entityId.ToId());
            await Request_Player_AddCredits(new IdCredits { id = playerId, credits = -costs });
        }

        private async Task DisplayHelp(int playerId)
        {
            var P = await Request_Player_Info(playerId.ToId());

            var info = new StringBuilder();
            if(!string.IsNullOrEmpty(P.bpInFactory) && P.bpRemainingTime > 0)
            {
                var remainingTime = new TimeSpan((long)(P.bpRemainingTime * TimeSpan.TicksPerSecond));
                info.AppendLine($"Finish '[c][00ff00]{BpName(P.bpInFactory)}[-][/c]' in factory for [c][00ff00]{remainingTime:h'h 'mm'm'}[-][/c] and costs [c][00ff00]{(int)(remainingTime.TotalMinutes * Configuration.Current.CostsPerRemainingMinute)}[-][/c] credits");
            }

            if (P.bpResourcesInFactory?.Any() == true)
            {
                info.Append("Rebuy ressources from factory costs [c][00ff00]");
                info.Append(P.bpResourcesInFactory.Aggregate(0.0, (s, r) => r.Value * (Configuration.Current.Ressources.FirstOrDefault(res => res.Item == r.Key)?.RebuyCostPerUnit ?? Configuration.Current.RebuyCostPerUnit) + s));
                info.AppendLine("[-][/c] credits");

                info.Append("Extraction of the resources results in approx lost [c][00ff00]");
                info.AppendFormat("{0:P0}[-][/c]", P.bpResourcesInFactory.Aggregate(0.0, (s, r) => ((Configuration.Current.Ressources.FirstOrDefault(res => res.Item == r.Key)?.ExtractionPercentLost ?? Configuration.Current.ExtractionPercentLost) + s) / 2.0));
            }

            await DisplayHelp(playerId, info.ToString());
        }

        private static string BpName(string name)
        {
            var workshopDelimiterPos = name.LastIndexOf("__");
            return workshopDelimiterPos > 0 ? name.Substring(0, workshopDelimiterPos) : name;
        }

        private void LoadConfiguration()
        {
            ConfigurationManager<Configuration>.Log = Log;
            Configuration = new ConfigurationManager<Configuration>() { ConfigFilename = Path.Combine(EmpyrionConfiguration.SaveGameModPath, "Configuration.json") };
            Configuration.CreateDefaults = config => {
                config.Ressources = new List<RessourcesData>(new[] { new RessourcesData
                    {
                        Item                    = 4320,
                        Name                    = "Iron",
                        RebuyCostPerUnit        = 5,
                        ExtractionPercentLost   = 0.3
                    }
                });
            };

            Configuration.Load();
            Configuration.Save();
        }
    }
}
