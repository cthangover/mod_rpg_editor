namespace Cthangover.Tools.RpgEditor.Tests.Catalog
{
    using System.Text.Json;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Mods.Testing;

    /// <summary>Optional absence, strict cache reads and malformed definitions are tested with the real runtime logger.</summary>
    public sealed class EntityResolverCollectionTests
    {
        [ModTest("catalog/optional_absence_cannot_hide_required_read")]
        static void OptionalAbsence(ModTestContext ctx)
        {
            using var probe = new OptionalCatalogProbe(ctx.SandboxDir);
            ModAssert.Equal(0, Entities.Collect<OptionalCatalogSample>(required: false).Count, "optional catalogue is empty");
            ModAssert.Equal(0, probe.Errors.Count, "optional absence emits no error");
            ModAssert.Equal(0, Entities.Collect<OptionalCatalogSample>().Count, "required lookup sees the same absence");
            ModAssert.Equal(1, probe.Errors.Count, "required cached lookup reports absence");
            ModAssert.True(probe.Errors[0].Contains("no data found"), "missing data is named");
            ModAssert.Equal(0, Entities.Collect<OptionalCatalogSample>().Count, "another required lookup is empty");
            ModAssert.Equal(1, probe.Errors.Count, "absence is reported once per generation");
            probe.ClearCache();
            ModAssert.Equal(0, Entities.Collect<OptionalCatalogSample>().Count, "new generation remains empty");
            ModAssert.Equal(2, probe.Errors.Count, "new generation reports absence again");
        }

        [ModTest("catalog/available_optional_data_roundtrips")]
        static void AvailableOptional(ModTestContext ctx) => Available(ctx, false);

        [ModTest("catalog/available_required_data_roundtrips")]
        static void AvailableRequired(ModTestContext ctx) => Available(ctx, true);

        [ModTest("catalog/optional_collection_keeps_malformed_data_errors")]
        static void Malformed(ModTestContext ctx)
        {
            using var probe = new OptionalCatalogProbe(ctx.SandboxDir);
            probe.Write(@"{""Kind"":""optional_catalog_sample"",""Items"":[{""Id"":""author/broken"",""Value"":""invalid""}]}");
            ModAssert.Equal(0, Entities.Collect<OptionalCatalogSample>(required: false).Count, "invalid item is not a profile");
            ModAssert.Equal(1, probe.Errors.Count, "invalid JSON field is still an error");
            ModAssert.True(probe.Errors[0].Contains("JSON parse failed"), "parse error diagnostic remains");
            ModAssert.True(probe.Errors[0].Contains("profiles.json"), "the broken definition is named");
            ModAssert.False(probe.Errors[0].Contains("no data found"), "optional absence adds no spurious error");
        }

        private static void Available(ModTestContext ctx, bool required)
        {
            using var probe = new OptionalCatalogProbe(ctx.SandboxDir);
            probe.Write(@"{""Kind"":""optional_catalog_sample"",""Items"":[{""Id"":""author/profile"",""Value"":37}]}");
            var entries = Entities.Collect<OptionalCatalogSample>(required);
            ModAssert.Equal(1, entries.Count, "one profile resolves");
            ModAssert.Equal("author/profile", entries["author/profile"].ID, "profile identity");
            ModAssert.Equal(37, entries["author/profile"].Value, "profile value");
            var restored = JsonSerializer.Deserialize<OptionalCatalogSample>(JsonSerializer.Serialize(entries["author/profile"]));
            ModAssert.Equal("author/profile", restored.ID, "identity survives JSON");
            ModAssert.Equal(37, restored.Value, "value survives JSON");
            ModAssert.Equal(0, probe.Errors.Count, "valid data has no errors");
        }
    }
}
