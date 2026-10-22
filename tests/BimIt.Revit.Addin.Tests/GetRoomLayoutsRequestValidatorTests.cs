using BimIt.Revit.Addin.Inspection;
using BimIt.Revit.Bridge;
using BimIt.Revit.Contracts;
using Xunit;

namespace BimIt.Revit.Addin.Tests;

public sealed class GetRoomLayoutsRequestValidatorTests
{
    [Fact]
    public void Null_document_or_refs_are_invalid()
    {
        AssertInvalid(new GetRoomLayoutsRequest { DocumentId = null, ElementRefs = ["room-1"] });
        AssertInvalid(new GetRoomLayoutsRequest { DocumentId = "doc", ElementRefs = null });
        AssertInvalid(new GetRoomLayoutsRequest { DocumentId = "doc", ElementRefs = [] });
        AssertInvalid(new GetRoomLayoutsRequest { DocumentId = "doc", ElementRefs = Enumerable.Range(0, 21).Select(index => $"room-{index}").ToArray() });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_document_id_is_present_and_accepted(string documentId)
    {
        var validated = GetRoomLayoutsRequestValidator.Validate(new GetRoomLayoutsRequest { DocumentId = documentId, ElementRefs = ["room-1"] });
        Assert.Equal(documentId, validated.DocumentId);
    }

    [Fact]
    public void Twenty_distinct_refs_are_accepted()
    {
        var refs = Enumerable.Range(0, 20).Select(index => $"room-{index}").ToArray();
        var validated = GetRoomLayoutsRequestValidator.Validate(new GetRoomLayoutsRequest { DocumentId = "doc", ElementRefs = refs });
        Assert.Equal(refs, validated.ElementRefs);
    }

    [Fact]
    public void Duplicate_null_and_overlong_refs_are_invalid()
    {
        AssertInvalid(new GetRoomLayoutsRequest { DocumentId = "doc", ElementRefs = ["room-1", "room-1"] });
        AssertInvalid(new GetRoomLayoutsRequest { DocumentId = "doc", ElementRefs = [null!] });
        AssertInvalid(new GetRoomLayoutsRequest { DocumentId = "doc", ElementRefs = [new string('a', 129)] });
        var validated = GetRoomLayoutsRequestValidator.Validate(new GetRoomLayoutsRequest
        {
            DocumentId = "doc",
            ElementRefs = ["Room-1", "room-1", new string('b', 128)],
        });
        Assert.Equal(3, validated.ElementRefs.Count);
    }

    private static void AssertInvalid(GetRoomLayoutsRequest request)
    {
        var exception = Assert.Throws<BridgeException>(() => GetRoomLayoutsRequestValidator.Validate(request));
        Assert.Equal(CapabilityErrorCodes.InvalidRoomLayout, exception.ErrorCode);
    }
}
