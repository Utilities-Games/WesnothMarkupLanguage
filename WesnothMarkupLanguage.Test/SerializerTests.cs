using System.Collections.Generic;
using Xunit;

namespace WesnothMarkupLanguage.Test
{
    public class SerializerTests
    {
        [WmlTag("unit_type")]
        public class Unit
        {
            [WmlAttribute("id")] public string Id { get; set; } = "";
            [WmlAttribute("cost")] public int Cost { get; set; }
            [WmlAttribute("male")] public bool Male { get; set; }
            [WmlChild("attack")] public List<Attack> Attacks { get; set; } = new List<Attack>();
            [WmlExtensionData] public Dictionary<string, string> Extra { get; set; } = new Dictionary<string, string>();
        }
        public class Attack { [WmlAttribute("damage")] public int Damage { get; set; } }

        [Fact] public void Poco_round_trip_supports_children_and_extension_data()
        {
            var source = new Unit { Id = "Fighter", Cost = 14, Male = true, Attacks = { new Attack { Damage = 6 } }, Extra = { ["custom"] = "ok" } };
            var result = WmlSerializer.Deserialize<Unit>(WmlSerializer.Serialize(source));
            Assert.Equal("Fighter", result.Id); Assert.Equal(14, result.Cost); Assert.True(result.Male); Assert.Equal(6, result.Attacks[0].Damage); Assert.Equal("ok", result.Extra["custom"]);
        }
        [Fact] public void Conversion_failure_has_source_context() { var doc = WmlParser.Parse("[unit_type]\ncost=nope\n[/unit_type]", "bad.cfg").Document; var error = Assert.Throws<WmlException>(() => WmlSerializer.Deserialize<Unit>(doc)); Assert.Contains("bad.cfg", error.Message); }
    }
}
