using BimIt.Revit.Server;
using Xunit;

namespace BimIt.Revit.Server.Tests;

public sealed class GetRoomLayoutsRoutingTests
{
    [Fact]
    public void Room_layouts_are_eligible_only_on_protocol_17()
    {
        Assert.False(InstanceTargetResolver.IsGetRoomLayoutsEligible(TestSupport.Ready("v16", "pipe-v16", protocolVersion: 16)));
        Assert.True(InstanceTargetResolver.IsGetRoomLayoutsEligible(TestSupport.Ready("v17", "pipe-v17", protocolVersion: 17)));
        Assert.False(InstanceTargetResolver.IsGetRoomLayoutsEligible(TestSupport.Ready("v18", "pipe-v18", protocolVersion: 18)));
        Assert.True(InstanceTargetResolver.IsGetSelectionEligible(TestSupport.Ready("v17", "pipe-v17", protocolVersion: 17)));
        Assert.True(InstanceTargetResolver.IsApplyMepDevicePlacementsEligible(TestSupport.Ready("v17", "pipe-v17", protocolVersion: 17)));
    }

    [Fact]
    public void Input_schema_is_closed_and_bounded()
    {
        var schema = GetRoomLayoutsJsonSchemas.Input;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(1, schema.GetProperty("properties").GetProperty("element_refs").GetProperty("minItems").GetInt32());
        Assert.Equal(20, schema.GetProperty("properties").GetProperty("element_refs").GetProperty("maxItems").GetInt32());
        Assert.Equal(128, schema.GetProperty("properties").GetProperty("element_refs").GetProperty("items").GetProperty("maxLength").GetInt32());
        Assert.Equal("m", GetRoomLayoutsJsonSchemas.Output.GetProperty("properties").GetProperty("length_unit").GetProperty("const").GetString());
        Assert.Equal(
            4,
            GetRoomLayoutsJsonSchemas.Output
                .GetProperty("properties")
                .GetProperty("items")
                .GetProperty("items")
                .GetProperty("properties")
                .GetProperty("loops")
                .GetProperty("maxItems")
                .GetInt32());
        var text = schema.GetRawText() + GetRoomLayoutsJsonSchemas.Output.GetRawText();
        Assert.DoesNotContain("feet", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("revit_get_room_layouts", GetRoomLayoutsToolMetadata.Name);
    }
}
