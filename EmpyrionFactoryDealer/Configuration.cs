using EmpyrionNetAPIDefinitions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace EmpyrionGalaxyNavigator
{
    [Serializable]
    public class RessourcesData
    {
        public int Item { get; set; }
        public string Name { get; set; }
        public int RebuyCostPerUnit { get; set; }
        public double ExtractionPercentLost { get; set; }
    }

    [Serializable]
    public class Configuration
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public LogLevel LogLevel { get; set; } = LogLevel.Message;
        public string ChatCommandPrefix { get; set; } = "/\\";
        public int CostsPerRemainingMinute { get; set; } = 10000;
        public int RebuyCostPerUnit { get; set; } = 10;
        public double ExtractionPercentLost { get; set; } = 0.2;
        public string NameIdMappingFile { get; set; } = "filepath to the NameIdMapping.json e.g. from EmpyrionScripting for item name support";
        public List<RessourcesData> Ressources { get; set; } = new List<RessourcesData>();
    }
}
