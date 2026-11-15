using System.Text.Json.Serialization;
using Cthangover.Core.Factories;

namespace Cthangover.Tools.RpgEditor.Tests.Catalog
{
    /// <summary>A separate entity kind keeps collection tests independent of installed game content.</summary>
    [EntityKind("optional_catalog_sample")]
    public sealed class OptionalCatalogSample : IIdentifiable
    {
        /// <summary>Written as Id, matching real entity envelopes.</summary>
        [JsonPropertyName("Id")]
        public string ID { get; set; }
        /// <summary>Requires numeric input so malformed named data exercises the deserializer.</summary>
        public int Value { get; set; }
    }
}
