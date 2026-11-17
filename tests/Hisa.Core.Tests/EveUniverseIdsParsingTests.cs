using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class EveUniverseIdsParsingTests
{
    [Fact]
    public void MatchCharacterId_FindsExactNameIgnoreCase()
    {
        const string json =
            """
            {
              "characters": [
                { "id": 2112625428, "name": "Sir Yohnny" },
                { "id": 1, "name": "Other Pilot" }
              ]
            }
            """;
        Assert.Equal(2112625428, EveUniverseIdsParsing.MatchCharacterId(json, "sir yohnny"));
        Assert.Equal(2112625428, EveUniverseIdsParsing.MatchCharacterId(json, "Sir Yohnny"));
    }

    [Fact]
    public void MatchCharacterId_ReturnsNullWhenMissingOrUnmatched()
    {
        Assert.Null(EveUniverseIdsParsing.MatchCharacterId("""{"alliances":[]}""", "Sir Yohnny"));
        Assert.Null(EveUniverseIdsParsing.MatchCharacterId(
            """{"characters":[{"id":1,"name":"Other"}]}""", "Sir Yohnny"));
        Assert.Null(EveUniverseIdsParsing.MatchCharacterId("{}", "Sir Yohnny"));
        Assert.Null(EveUniverseIdsParsing.MatchCharacterId(null, "Sir Yohnny"));
        Assert.Null(EveUniverseIdsParsing.MatchCharacterId("""{"characters":[]}""", "  "));
    }
}
